using FunEvents.Domain.Common;

namespace FunEvents.Domain.Reservations;

/// <summary>
/// Reserva de entradas: una retencion temporal de cupo que caduca si no se
/// confirma. El cupo real vive en <c>Event.ReservedCount</c>; esta entidad es
/// el registro de quien lo retiene, por que canal y hasta cuando.
/// </summary>
public class Reservation : BaseEntity
{
    public Guid EventId { get; private set; }

    /// <summary>Comprador. Obligatorio en los tres canales: incluso una venta en
    /// oficina o via partner se atribuye a un usuario concreto.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Colaborador que origino la venta, solo en el canal Partner.</summary>
    public Guid? PartnerId { get; private set; }

    public int TicketQuantity { get; private set; }
    public ReservationState State { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public SalesChannel Channel { get; private set; }

    // Requerido por EF Core para materializar desde la base de datos.
    private Reservation() { }

    public Reservation(Guid eventId, Guid userId, int ticketQuantity, DateTimeOffset expiresAt,
        SalesChannel channel, Guid? partnerId = null)
    {
        if (eventId == Guid.Empty)
            throw new DomainException("EventId is required.", ReservationErrors.InvalidEvent);
        if (userId == Guid.Empty)
            throw new DomainException("UserId is required.", ReservationErrors.InvalidUser);
        if (ticketQuantity <= 0)
            throw new DomainException("Ticket quantity must be greater than zero.", ReservationErrors.InvalidQuantity);
        if (channel == SalesChannel.Partner && partnerId is null)
            throw new DomainException("PartnerId is required for the Partner channel.", ReservationErrors.InvalidPartner);
        if (channel != SalesChannel.Partner && partnerId is not null)
            throw new DomainException("PartnerId is only valid for the Partner channel.", ReservationErrors.InvalidPartner);

        EventId = eventId;
        UserId = userId;
        TicketQuantity = ticketQuantity;
        State = ReservationState.Reserved;
        ExpiresAt = expiresAt;
        Channel = channel;
        PartnerId = partnerId;
    }

    /// <summary>Una reserva "ocupa cupo" mientras esta retenida o confirmada.</summary>
    public bool HoldsCapacity() => State is ReservationState.Reserved or ReservationState.Confirmed;

    public bool IsExpired(DateTimeOffset? now = null)
        => State == ReservationState.Reserved && (now ?? DateTimeOffset.UtcNow) >= ExpiresAt;

    public void Confirm(DateTimeOffset? now = null)
    {
        if (State != ReservationState.Reserved)
            throw new DomainException(
                $"Only reserved reservations can be confirmed (current state: {State}).",
                ReservationErrors.NotReserved);
        if (IsExpired(now))
            throw new DomainException("Cannot confirm an expired reservation.", ReservationErrors.AlreadyExpired);

        State = ReservationState.Confirmed;
        Touch();
    }

    public void MarkExpired()
    {
        if (State != ReservationState.Reserved)
            throw new DomainException(
                $"Only reserved reservations can expire (current state: {State}).",
                ReservationErrors.NotReserved);

        State = ReservationState.Expired;
        Touch();
    }

    /// <summary>
    /// Cancelacion explicita. Admite tanto Reserved como Confirmed: el usuario
    /// puede desistir antes de pagar y la operacion puede anular una venta ya
    /// confirmada. La version anterior solo permitia cancelar Confirmed, lo que
    /// dejaba las reservas Reserved sin salida manual (solo caducidad).
    /// </summary>
    public void Cancel()
    {
        if (State is ReservationState.Expired or ReservationState.Cancelled)
            throw new DomainException(
                $"Reservation is already in a terminal state ({State}).",
                ReservationErrors.NotReserved);

        State = ReservationState.Cancelled;
        Touch();
    }
}
