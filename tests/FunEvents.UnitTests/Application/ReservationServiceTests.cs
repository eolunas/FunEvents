using FluentAssertions;
using FunEvents.Application.Common;
using FunEvents.Application.Reservations;
using FunEvents.Application.Reservations.Dtos;
using FunEvents.Domain.Common;
using FunEvents.Domain.Events;
using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace FunEvents.UnitTests.Application;

/// <summary>
/// Reglas del caso de uso de reserva, con dobles de prueba en lugar de base de
/// datos.
/// </summary>
/// <remarks>
/// Estos tests cubren precisamente los caminos que antes no tenian ninguna
/// cobertura y donde estaban los fallos: liberacion de la Idempotency-Key
/// cuando la operacion falla, reutilizacion de una key con otro cuerpo, y
/// limite de entradas por usuario.
/// </remarks>
public class ReservationServiceTests
{
    private static readonly Guid EventId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private const string Key = "test-idempotency-key";

    private readonly IEventRepository _events = Substitute.For<IEventRepository>();
    private readonly IReservationRepository _reservations = Substitute.For<IReservationRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IIdempotencyStore _idempotency = Substitute.For<IIdempotencyStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly ReservationPolicyOptions _policy = new()
    {
        MaxTicketsPerRequest = 10,
        MaxTicketsPerUserPerEvent = 4,
        HoldWindow = TimeSpan.FromMinutes(15),
        IdempotencyWaitTimeout = TimeSpan.FromMilliseconds(200),
        IdempotencyPollInterval = TimeSpan.FromMilliseconds(20)
    };

    private ReservationService BuildSut() => new(
        _events, _reservations, _users, _idempotency, _unitOfWork,
        Options.Create(_policy), NullLogger<ReservationService>.Instance, TimeProvider.System);

    public ReservationServiceTests()
    {
        // El doble de IUnitOfWork ejecuta la operacion tal cual. Lo que se
        // verifica aqui es la logica del caso de uso; que la transaccion haga
        // commit o rollback de verdad es responsabilidad de los tests de
        // integracion contra PostgreSQL.
        _unitOfWork
            .ExecuteInTransactionAsync<CreateReservationResult>(
                Arg.Any<Func<CancellationToken, Task<CreateReservationResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<CreateReservationResult>>>()
                (CancellationToken.None));

        _idempotency.TryBeginAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(ActiveUser());

        _events.GetByIdAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(PublishedEvent());

        _events.TryReserveCapacityAsync(EventId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _reservations.CountActiveTicketsAsync(UserId, EventId, Arg.Any<CancellationToken>())
            .Returns(0);
    }

    // -----------------------------------------------------------------
    // Camino feliz
    // -----------------------------------------------------------------

    [Fact]
    public async Task Crea_la_reserva_y_registra_la_respuesta_idempotente()
    {
        var result = await BuildSut().CreateAsync(Request(2), Key);

        result.Replayed.Should().BeFalse();
        result.Reservation.TicketQuantity.Should().Be(2);
        result.Reservation.EventId.Should().Be(EventId);
        result.Reservation.UserId.Should().Be(UserId);
        result.Reservation.State.Should().Be(nameof(ReservationState.Reserved));

        await _reservations.Received(1).AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
        await _idempotency.Received(1).CompleteAsync(
            Key, Arg.Any<Guid>(), 201, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------
    // Reglas de negocio
    // -----------------------------------------------------------------

    [Fact]
    public async Task Sin_aforo_lanza_InsufficientCapacity_y_no_crea_nada()
    {
        _events.TryReserveCapacityAsync(EventId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.InsufficientCapacity);

        await _reservations.DidNotReceive().AddAsync(Arg.Any<Reservation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_usuario_inexistente_lanza_UserNotFound()
    {
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.UserNotFound);
    }

    [Fact]
    public async Task Un_usuario_inactivo_lanza_UserInactive()
    {
        var user = ActiveUser();
        user.Deactivate();
        _users.GetByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.UserInactive);
    }

    [Fact]
    public async Task Un_evento_sin_publicar_lanza_EventNotPublished()
    {
        // Evento en borrador: no llama a Publish().
        _events.GetByIdAsync(EventId, Arg.Any<CancellationToken>()).Returns(DraftEvent());

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.EventNotPublished);

        await _events.DidNotReceive().TryReserveCapacityAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Superar_el_limite_por_usuario_lanza_PerUserLimitExceeded()
    {
        // El limite configurado es 4 y el usuario ya retiene 3.
        _reservations.CountActiveTicketsAsync(UserId, EventId, Arg.Any<CancellationToken>())
            .Returns(3);

        var act = async () => await BuildSut().CreateAsync(Request(quantity: 2), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.PerUserLimitExceeded);

        await _events.DidNotReceive().TryReserveCapacityAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Justo_en_el_limite_por_usuario_la_reserva_se_acepta()
    {
        _reservations.CountActiveTicketsAsync(UserId, EventId, Arg.Any<CancellationToken>())
            .Returns(3);

        var result = await BuildSut().CreateAsync(Request(quantity: 1), Key);

        result.Replayed.Should().BeFalse();
    }

    // -----------------------------------------------------------------
    // Idempotencia
    // -----------------------------------------------------------------

    [Fact]
    public async Task Un_fallo_libera_la_Idempotency_Key_para_que_el_cliente_pueda_reintentar()
    {
        // Regresion del fallo mas sutil que tenia el flujo: si la operacion
        // fallaba tras tomar la key, la key quedaba retenida y el mismo cliente
        // no podia reintentar NUNCA con ella.
        _events.TryReserveCapacityAsync(EventId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        await act.Should().ThrowAsync<DomainException>();
        await _idempotency.Received(1).ReleaseAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reutilizar_una_key_con_otro_cuerpo_se_rechaza()
    {
        _idempotency.GetAsync(Key, Arg.Any<CancellationToken>())
            .Returns(new IdempotencyRecord
            {
                Key = Key,
                RequestFingerprint = "una-huella-completamente-distinta",
                ReservationId = Guid.NewGuid(),
                ResponseBody = "{}",
                StatusCode = 201,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });

        var act = async () => await BuildSut().CreateAsync(Request(), Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.IdempotencyKeyReused);

        await _events.DidNotReceive().TryReserveCapacityAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_otra_peticion_retiene_la_key_y_no_termina_se_responde_RequestInProgress()
    {
        var request = Request();

        // La key ya esta tomada por otra peticion que nunca completa.
        //
        // La huella TIENE que coincidir con la de esta peticion: el servicio
        // comprueba la reutilizacion de key ANTES que el estado de la key, asi
        // que con una huella distinta este escenario devolveria
        // IDEMPOTENCY_KEY_REUSED y nunca llegaria a la espera que queremos probar.
        _idempotency.GetAsync(Key, Arg.Any<CancellationToken>())
            .Returns(new IdempotencyRecord
            {
                Key = Key,
                RequestFingerprint = FingerprintOf(request),
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = null   // sigue en curso
            });

        var act = async () => await BuildSut().CreateAsync(request, Key);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.ErrorCode.Should().Be(ReservationErrors.RequestInProgress);
    }

    // -----------------------------------------------------------------
    // Utilidades
    // -----------------------------------------------------------------

    private static CreateReservationRequest Request(int quantity = 1) => new()
    {
        EventId = EventId,
        UserId = UserId,
        TicketQuantity = quantity,
        Channel = SalesChannel.Online
    };

    /// <summary>
    /// Misma huella que calcula <c>ReservationService</c> para esta peticion.
    /// Si cambian los campos que entran en la huella, este helper falla a la vez
    /// que el servicio y el test lo delata.
    /// </summary>
    private static string FingerprintOf(CreateReservationRequest request)
        => RequestFingerprint.Compute(
            request.EventId, request.UserId, request.TicketQuantity, request.Channel, request.PartnerId);

    private static User ActiveUser() => new("Ana Martinez", "ana.martinez@example.com") { Id = UserId };

    private static Event DraftEvent() => new(
        "FunFest 2026", "desc", "Estadio Nacional",
        DateTimeOffset.UtcNow.AddDays(30), DateTimeOffset.UtcNow.AddDays(30).AddHours(6), 100)
    { Id = EventId };

    private static Event PublishedEvent()
    {
        var @event = DraftEvent();
        @event.Publish();
        return @event;
    }
}
