using System.Text.Json;
using FunEvents.Application.Common;
using FunEvents.Application.Reservations.Dtos;
using FunEvents.Domain.Common;
using FunEvents.Domain.Events;
using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunEvents.Application.Reservations;

/// <summary>
/// Caso de uso central de la prueba: reservar entradas.
/// </summary>
/// <remarks>
/// <para><b>El flujo tiene tres fases y cada una resuelve un problema distinto.</b></para>
/// <para>
/// <b>Fase 1 - Reproduccion.</b> Si la Idempotency-Key ya termino, se devuelve
/// la respuesta guardada. Un reintento por timeout de red no crea una segunda
/// reserva.
/// </para>
/// <para>
/// <b>Fase 2 - Exclusion mutua.</b> Se intenta tomar la key. La clave primaria
/// de la tabla decide el ganador; el resto espera brevemente al resultado en
/// lugar de duplicar el trabajo. Esta fase va FUERA de la transaccion a
/// proposito: en PostgreSQL, una violacion de unicidad aborta la transaccion
/// entera, asi que usarla como mecanismo de bloqueo dentro de la transaccion
/// del caso de uso invalidaria todo lo demas.
/// </para>
/// <para>
/// <b>Fase 3 - Trabajo real, en una sola transaccion.</b> Validaciones, consumo
/// atomico de aforo, alta de la reserva y registro de la respuesta idempotente
/// hacen commit juntos o no hace ninguno.
/// </para>
/// <para>
/// <b>Que estaba mal antes.</b> No habia transaccion: el contador de aforo se
/// incrementaba con un UPDATE inmediato y la reserva se insertaba despues, asi
/// que un fallo en medio dejaba aforo consumido por una reserva inexistente.
/// Ademas, si el registro de idempotencia apuntaba a una reserva que no se
/// encontraba, el codigo <i>fabricaba</i> un objeto Reservation nuevo y lo
/// devolvia como si fuera la reserva original: el cliente recibia un
/// ReservationId aleatorio que no existia en la base de datos.
/// </para>
/// </remarks>
public class ReservationService(
    IEventRepository events,
    IReservationRepository reservations,
    IUserRepository users,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    IOptions<ReservationPolicyOptions> policy,
    ILogger<ReservationService> logger,
    TimeProvider clock) : IReservationService
{
    private static readonly JsonSerializerOptions ReplayJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        var fingerprint = RequestFingerprint.Compute(
            request.EventId, request.UserId, request.TicketQuantity, request.Channel, request.PartnerId);

        // ---- Fase 1: reproduccion ----
        var known = await idempotency.GetAsync(idempotencyKey, ct);
        if (known is not null)
            return await ResolveKnownKeyAsync(known, idempotencyKey, fingerprint, ct);

        // ---- Fase 2: exclusion mutua ----
        if (!await idempotency.TryBeginAsync(idempotencyKey, fingerprint, ct))
        {
            var settled = await WaitForCompletionAsync(idempotencyKey, ct);

            if (settled is null)
                throw new DomainException(
                    "Another request with the same Idempotency-Key is still in progress. Retry shortly.",
                    ReservationErrors.RequestInProgress);

            return await ResolveKnownKeyAsync(settled, idempotencyKey, fingerprint, ct);
        }

        // ---- Fase 3: trabajo real ----
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync<CreateReservationResult>(
                token => ReserveAsync(request, idempotencyKey, token), ct);
        }
        catch
        {
            // La key se tomo pero la operacion fallo. Si no se libera, el cliente
            // no podria reintentar NUNCA con esa key: recibiria 409 hasta que la
            // purga la borrase 24 horas despues.
            //
            // CancellationToken.None a proposito: liberar la key debe ocurrir
            // incluso si el fallo fue precisamente una cancelacion.
            await idempotency.ReleaseAsync(idempotencyKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<ReservationResponse?> GetByIdAsync(Guid reservationId, CancellationToken ct = default)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, ct);
        if (reservation is null) return null;

        var @event = await events.GetByIdAsync(reservation.EventId, ct);
        var user = await users.GetByIdAsync(reservation.UserId, ct);

        return Map(reservation, @event, user, previouslyCreated: false);
    }
    // ------------------------------------------------------------------------

    /// <remarks>
    /// <b>Que estaba mal antes.</b> La URL se construia con
    /// <c>{channel}/{eventId}</c>: dos reservas distintas del mismo evento y
    /// canal (dos usuarios comprando la misma funcion por Online, por
    /// ejemplo) producian exactamente la misma URL. No identificaba una
    /// reserva, identificaba un evento. Ademas el metodo no estaba declarado
    /// en <see cref="IReservationService"/> -el controlador no compilaba- y
    /// devolvia un <see langword="string"/> suelto en vez de un DTO, con lo
    /// que no habia forma de aplicar el aislamiento entre colaboradores que
    /// si tiene <see cref="GetByIdAsync"/>.
    /// </remarks>
    public async Task<ReservationUrlResponse?> GetUrlByIdAsync(Guid reservationId, CancellationToken ct = default)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, ct);
        if (reservation is null) return null;

        var channel = Uri.EscapeDataString(reservation.Channel.ToString());
        var url = $"{policy.Value.ReservationUrlBase.TrimEnd('/')}/{channel}/{reservation.Id}";

        return new ReservationUrlResponse
        {
            ReservationId = reservation.Id,
            PartnerId = reservation.PartnerId,
            Url = url
        };
    }

    // ------------------------------------------------------------------

    private async Task<CreateReservationResult> ReserveAsync(
        CreateReservationRequest request, string idempotencyKey, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(request.UserId, ct)
                   ?? throw new DomainException(
                       $"User {request.UserId} not found.", ReservationErrors.UserNotFound);

        if (!user.IsActive)
            throw new DomainException(
                $"User {request.UserId} is not active.", ReservationErrors.UserInactive);

        var @event = await events.GetByIdAsync(request.EventId, ct)
                     ?? throw new DomainException(
                         $"Event {request.EventId} not found.", ReservationErrors.EventNotFound);

        if (!@event.IsOpenForSale())
            throw new DomainException(
                $"Event '{@event.Name}' is not open for sale (state: {@event.State}).",
                ReservationErrors.EventNotPublished);

        await EnforcePerUserLimitAsync(request, ct);

        // Punto critico de concurrencia: comprobar y consumir el aforo en una
        // sola sentencia. Si devuelve false, otro comprador se llevo las plazas
        // entre esta peticion y la anterior.
        var reserved = await events.TryReserveCapacityAsync(
            request.EventId, request.TicketQuantity, ct);

        if (!reserved)
            throw new DomainException(
                $"Not enough capacity left for event '{@event.Name}'.",
                ReservationErrors.InsufficientCapacity);

        var reservation = new Reservation(
            eventId: request.EventId,
            userId: request.UserId,
            ticketQuantity: request.TicketQuantity,
            expiresAt: clock.GetUtcNow().Add(policy.Value.HoldWindow),
            channel: request.Channel,
            partnerId: request.PartnerId);

        await reservations.AddAsync(reservation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var response = Map(reservation, @event, user, previouslyCreated: false);

        // Se guarda la respuesta EXACTA dentro de la misma transaccion: si el
        // commit no llega, no queda rastro de la key y el reintento del cliente
        // se procesa desde cero.
        await idempotency.CompleteAsync(
            idempotencyKey,
            reservation.Id,
            statusCode: 201,
            responseBody: JsonSerializer.Serialize(response, ReplayJsonOptions),
            ct);

        logger.LogInformation(
            "Reservation {ReservationId} created: {Quantity} ticket(s) for event {EventId} " +
            "by user {UserId} via {Channel}, expires at {ExpiresAt:u}",
            reservation.Id, reservation.TicketQuantity, reservation.EventId,
            reservation.UserId, reservation.Channel, reservation.ExpiresAt);

        return new CreateReservationResult(response, Replayed: false);
    }

    private async Task EnforcePerUserLimitAsync(CreateReservationRequest request, CancellationToken ct)
    {
        var limit = policy.Value.MaxTicketsPerUserPerEvent;
        if (limit <= 0) return;

        var alreadyHeld = await reservations.CountActiveTicketsAsync(
            request.UserId, request.EventId, ct);

        if (alreadyHeld + request.TicketQuantity <= limit) return;

        throw new DomainException(
            $"User already holds {alreadyHeld} ticket(s) for this event; the limit is {limit}.",
            ReservationErrors.PerUserLimitExceeded);

        // NOTA DE HONESTIDAD TECNICA: contar y despues insertar deja una ventana
        // teorica en la que dos peticiones simultaneas del MISMO usuario podrian
        // superar el limite entre ambas. No es sobreventa (el aforo global sigue
        // protegido por el UPDATE atomico), solo un usuario que se pasa del tope.
        // Cerrarlo por completo exige un lock por (usuario, evento) -por ejemplo
        // pg_advisory_xact_lock- y se documenta como Fase 2 en vez de fingir que
        // el problema no existe.
    }

    /// <summary>
    /// Espera acotada a que otra peticion con la misma key termine.
    /// </summary>
    private async Task<IdempotencyRecord?> WaitForCompletionAsync(string key, CancellationToken ct)
    {
        var deadline = clock.GetUtcNow() + policy.Value.IdempotencyWaitTimeout;

        while (clock.GetUtcNow() < deadline)
        {
            await Task.Delay(policy.Value.IdempotencyPollInterval, ct);

            var record = await idempotency.GetAsync(key, ct);

            // Desaparecio: la peticion ganadora fallo y libero la key.
            // Que el cliente reintente; no adivinamos por el.
            if (record is null) return null;

            if (record.IsCompleted) return record;
        }

        logger.LogWarning(
            "Timed out waiting for in-flight request with Idempotency-Key {Key}", key);
        return null;
    }

    private async Task<CreateReservationResult> ResolveKnownKeyAsync(
        IdempotencyRecord record, string key, string fingerprint, CancellationToken ct)
    {
        if (!string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new DomainException(
                "This Idempotency-Key was already used with a different request body.",
                ReservationErrors.IdempotencyKeyReused);

        if (!record.IsCompleted)
        {
            var settled = await WaitForCompletionAsync(key, ct);
            if (settled is null)
                throw new DomainException(
                    "Another request with the same Idempotency-Key is still in progress. Retry shortly.",
                    ReservationErrors.RequestInProgress);

            record = settled;
        }

        // La reserva pudo caducar entre la peticion original y este reintento,
        // asi que se relee el estado actual en vez de devolver el congelado.
        if (record.ReservationId is { } reservationId)
        {
            var current = await GetByIdAsync(reservationId, ct);
            if (current is not null)
                return new CreateReservationResult(current with { PreviouslyCreated = true }, Replayed: true);

            logger.LogError(
                "Idempotency key {Key} points to reservation {ReservationId}, which no longer exists",
                key, reservationId);
        }

        var stored = JsonSerializer.Deserialize<ReservationResponse>(record.ResponseBody!, ReplayJsonOptions)
                     ?? throw new InvalidOperationException(
                         $"Stored idempotent response for key '{key}' could not be deserialized.");

        return new CreateReservationResult(stored with { PreviouslyCreated = true }, Replayed: true);
    }

    private static ReservationResponse Map(
        Reservation reservation, Event? @event, User? user, bool previouslyCreated) => new()
    {
        ReservationId = reservation.Id,
        EventId = reservation.EventId,
        EventName = @event?.Name ?? string.Empty,
        UserId = reservation.UserId,
        UserName = user?.FullName ?? string.Empty,
        TicketQuantity = reservation.TicketQuantity,
        State = reservation.State.ToString(),
        Channel = reservation.Channel.ToString(),
        PartnerId = reservation.PartnerId,
        ExpiresAt = reservation.ExpiresAt,
        CreatedAt = reservation.CreatedAt,
        PreviouslyCreated = previouslyCreated
    };
}
