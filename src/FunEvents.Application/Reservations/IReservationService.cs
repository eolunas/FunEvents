using FunEvents.Application.Reservations.Dtos;

namespace FunEvents.Application.Reservations;

public interface IReservationService
{
    /// <summary>
    /// Crea una reserva de forma idempotente respecto a
    /// <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <param name="caller">
    /// Identidad de quien llama. Solo importa para el canal
    /// <see cref="Domain.Common.SalesChannel.Partner"/>, que exige credencial y
    /// permiso de creacion; se ignora en el resto de canales. Por defecto,
    /// anonimo.
    /// </param>
    /// <exception cref="Domain.Common.DomainException">
    /// Cuando se incumple una regla de negocio. El <c>ErrorCode</c> indica cual
    /// (ver <see cref="Domain.Reservations.ReservationErrors"/>).
    /// </exception>
    Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, string idempotencyKey,
        ReservationCaller? caller = null, CancellationToken ct = default);

    Task<ReservationResponse?> GetByIdAsync(Guid reservationId, CancellationToken ct = default);

    /// <summary>
    /// Devuelve la URL publica de una reserva junto con los datos necesarios
    /// para que el llamante aplique el mismo aislamiento entre colaboradores
    /// que <see cref="GetByIdAsync"/>. <see langword="null"/> si la reserva no
    /// existe.
    /// </summary>
    Task<ReservationUrlResponse?> GetUrlByIdAsync(Guid reservationId, CancellationToken ct = default);
}
