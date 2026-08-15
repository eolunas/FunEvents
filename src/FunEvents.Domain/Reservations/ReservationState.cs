namespace FunEvents.Domain.Reservations;

/// <summary>
/// Ciclo de vida de una reserva.
/// <code>
/// Reserved  --confirmar-->  Confirmed
///    |                          |
///    |--caducar--> Expired      |--cancelar--> Cancelled
///    |--cancelar--> Cancelled
/// </code>
/// Expired y Cancelled son estados terminales.
/// </summary>
public enum ReservationState
{
    Reserved = 0,
    Confirmed = 1,
    Expired = 2,
    Cancelled = 3
}
