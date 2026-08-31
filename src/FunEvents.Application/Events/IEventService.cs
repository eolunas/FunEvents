using FunEvents.Application.Events.Dtos;

namespace FunEvents.Application.Events;

public interface IEventService
{
    /// <summary><see langword="null"/> si el evento no existe.</summary>
    Task<EventDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Catalogo de eventos publicados, paginado. <paramref name="page"/> y
    /// <paramref name="pageSize"/> fuera de rango se sanean, no se rechazan.
    /// </summary>
    Task<PagedResponse<EventDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default);
}
