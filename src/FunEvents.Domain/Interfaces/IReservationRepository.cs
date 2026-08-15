using FunEvents.Domain.Reservations;

namespace FunEvents.Domain.Interfaces;

public interface IReservationRepository
{
    Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Reservation reservation, CancellationToken ct = default);

    /// <summary>
    /// Entradas que un usuario ya retiene para un evento (Reserved + Confirmed).
    /// Sostiene el limite por usuario y evento.
    /// </summary>
    Task<int> CountActiveTicketsAsync(Guid userId, Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Reservas caducadas pendientes de procesar, tomadas con bloqueo de fila
    /// y saltando las que ya tenga bloqueadas otra instancia.
    /// </summary>
    /// <param name="batchSize">Tope de reservas por pasada.</param>
    Task<IReadOnlyList<Reservation>> ClaimExpiredAsync(int batchSize, CancellationToken ct = default);
}
