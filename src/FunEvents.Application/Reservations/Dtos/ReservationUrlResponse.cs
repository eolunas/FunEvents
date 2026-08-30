namespace FunEvents.Application.Reservations.Dtos;

/// <summary>
/// URL publica para retomar una reserva (por ejemplo, para reanudar el pago o
/// mostrar el ticket).
/// </summary>
/// <remarks>
/// Es un DTO propio -no un <see langword="string"/> suelto- por la misma razon
/// que el resto de respuestas del API: un valor primitivo no se puede
/// versionar (anadir un campo despues seria un cambio incompatible del
/// contrato JSON), y devolver <c>ReservationId</c>/<c>PartnerId</c> junto a la
/// URL es lo que permite al controlador aplicar el mismo aislamiento entre
/// colaboradores que ya se aplica en <c>GET /reservations/{id}</c>.
/// </remarks>
public record ReservationUrlResponse
{
    public Guid ReservationId { get; init; }

    /// <summary>Colaborador propietario de la reserva. Nulo en Online y Office.</summary>
    public Guid? PartnerId { get; init; }

    public string Url { get; init; } = string.Empty;
}
