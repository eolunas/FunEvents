using FluentAssertions;
using FunEvents.Domain.Common;
using FunEvents.Domain.Reservations;

namespace FunEvents.UnitTests.Domain;

/// <summary>
/// Reglas de la entidad Reservation. No tocan base de datos ni HTTP: verifican
/// que el dominio se protege solo, independientemente de quien lo llame.
/// </summary>
public class ReservationTests
{
    private static readonly Guid AnyEvent = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnyUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnyPartner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Reservation Create(
        int quantity = 1,
        TimeSpan? holdFor = null,
        SalesChannel channel = SalesChannel.Online,
        Guid? partnerId = null,
        Guid? userId = null)
        => new(
            eventId: AnyEvent,
            userId: userId ?? AnyUser,
            ticketQuantity: quantity,
            expiresAt: DateTimeOffset.UtcNow.Add(holdFor ?? TimeSpan.FromMinutes(15)),
            channel: channel,
            partnerId: partnerId);

    [Fact]
    public void Nace_en_estado_Reserved()
    {
        var reservation = Create(quantity: 2);

        reservation.State.Should().Be(ReservationState.Reserved);
        reservation.TicketQuantity.Should().Be(2);
        reservation.EventId.Should().Be(AnyEvent);
        reservation.UserId.Should().Be(AnyUser);
        reservation.HoldsCapacity().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Rechaza_cantidades_no_positivas(int quantity)
    {
        var act = () => Create(quantity: quantity);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.InvalidQuantity);
    }

    [Fact]
    public void Exige_usuario()
    {
        var act = () => Create(userId: Guid.Empty);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.InvalidUser);
    }

    [Fact]
    public void El_canal_Partner_exige_partnerId()
    {
        var act = () => Create(channel: SalesChannel.Partner, partnerId: null);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.InvalidPartner);
    }

    [Fact]
    public void Los_canales_no_Partner_no_admiten_partnerId()
    {
        var act = () => Create(channel: SalesChannel.Online, partnerId: AnyPartner);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.InvalidPartner);
    }

    [Fact]
    public void Confirmar_una_reserva_vigente_la_pasa_a_Confirmed()
    {
        var reservation = Create();

        reservation.Confirm();

        reservation.State.Should().Be(ReservationState.Confirmed);
        reservation.HoldsCapacity().Should().BeTrue("una reserva confirmada sigue ocupando aforo");
    }

    [Fact]
    public void No_se_puede_confirmar_una_reserva_caducada()
    {
        var reservation = Create(holdFor: TimeSpan.FromMinutes(-1));

        var act = () => reservation.Confirm();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.AlreadyExpired);
    }

    [Fact]
    public void No_se_puede_confirmar_dos_veces()
    {
        var reservation = Create();
        reservation.Confirm();

        var act = () => reservation.Confirm();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(ReservationErrors.NotReserved);
    }

    [Fact]
    public void Caducar_una_reserva_vigente_la_pasa_a_Expired_y_libera_aforo()
    {
        var reservation = Create();

        reservation.MarkExpired();

        reservation.State.Should().Be(ReservationState.Expired);
        reservation.HoldsCapacity().Should().BeFalse();
    }

    [Fact]
    public void Se_puede_cancelar_una_reserva_todavia_sin_confirmar()
    {
        // Regresion: antes Cancel() solo admitia el estado Confirmed, asi que
        // una reserva en Reserved no tenia salida manual y habia que esperar a
        // que caducase.
        var reservation = Create();

        reservation.Cancel();

        reservation.State.Should().Be(ReservationState.Cancelled);
    }

    [Fact]
    public void Se_puede_cancelar_una_reserva_confirmada()
    {
        var reservation = Create();
        reservation.Confirm();

        reservation.Cancel();

        reservation.State.Should().Be(ReservationState.Cancelled);
    }

    [Fact]
    public void No_se_puede_cancelar_dos_veces()
    {
        var reservation = Create();
        reservation.Cancel();

        var act = () => reservation.Cancel();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void IsExpired_usa_el_instante_que_se_le_pasa()
    {
        var reservation = Create(holdFor: TimeSpan.FromMinutes(15));

        reservation.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
        reservation.IsExpired(DateTimeOffset.UtcNow.AddMinutes(16)).Should().BeTrue();
    }
}
