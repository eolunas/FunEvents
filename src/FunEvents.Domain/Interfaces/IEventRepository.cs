using FunEvents.Domain.Events;

namespace FunEvents.Domain.Interfaces;

public interface IEventRepository
{
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<Event> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? search = null, Guid? partnerId = null, CancellationToken ct = default);

    Task AddAsync(Event @event, CancellationToken ct = default);

    /// <summary>
    /// Reserva <paramref name="quantity"/> plazas de forma atomica.
    /// </summary>
    /// <returns>
    /// <c>true</c> si el cupo se reservo; <c>false</c> si no habia capacidad
    /// suficiente o el evento no esta publicado.
    /// </returns>
    /// <remarks>
    /// Se traduce a un unico UPDATE condicional. La condicion
    /// <c>Capacity - ReservedCount &gt;= quantity</c> viaja al motor, de modo que
    /// la comprobacion y la escritura ocurren bajo el mismo row lock y no existe
    /// ventana entre "consultar disponibilidad" y "consumirla".
    /// </remarks>
    Task<bool> TryReserveCapacityAsync(Guid eventId, int quantity, CancellationToken ct = default);

    /// <summary>Devuelve plazas al aforo (caducidad o cancelacion).</summary>
    /// <returns><c>true</c> si se libero el cupo.</returns>
    Task<bool> ReleaseCapacityAsync(Guid eventId, int quantity, CancellationToken ct = default);
}
