using FunEvents.Application.Reservations.Dtos;

namespace FunEvents.Application.Reservations;

public interface IReservationService
{
    /// <summary>
    /// Crea una reserva de forma idempotente respecto a
    /// <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <exception cref="Domain.Common.DomainException">
    /// Cuando se incumple una regla de negocio. El <c>ErrorCode</c> indica cual
    /// (ver <see cref="Domain.Reservations.ReservationErrors"/>).
    /// </exception>
    Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, string idempotencyKey, CancellationToken ct = default);

    Task<ReservationResponse?> GetByIdAsync(Guid reservationId, CancellationToken ct = default);
}
