namespace FunEvents.Application.Reservations.Dtos;

/// <remarks>
/// Es un <c>record</c> y no una clase para poder usar <c>with</c> al reproducir
/// una respuesta idempotente: se clona la reserva actual cambiando unicamente
/// <see cref="PreviouslyCreated"/>, sin reescribir el mapeo campo a campo.
/// </remarks>
public record ReservationResponse
{
    public Guid ReservationId { get; init; }
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public int TicketQuantity { get; init; }
    public string State { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// Colaborador que origino la venta. Nulo en los canales Online y Office.
    /// No se acepta del cuerpo de la peticion: lo fija el servidor a partir de
    /// la API Key presentada, y es el campo sobre el que se aplica el
    /// aislamiento entre colaboradores al consultar una reserva.
    /// </summary>
    public Guid? PartnerId { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// <c>true</c> cuando la respuesta es la reproduccion de una peticion
    /// anterior con la misma Idempotency-Key, no una reserva nueva.
    /// </summary>
    public bool PreviouslyCreated { get; init; }
}

/// <summary>Resultado de intentar crear una reserva.</summary>
/// <param name="Reservation">La reserva, nueva o reproducida.</param>
/// <param name="Replayed">
/// <c>true</c> si la peticion se resolvio con una reserva ya existente
/// (la API responde 200 en vez de 201).
/// </param>
public sealed record CreateReservationResult(ReservationResponse Reservation, bool Replayed);
