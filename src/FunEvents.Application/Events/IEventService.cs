using FunEvents.Application.Events.Dtos;

namespace FunEvents.Application.Events;

public interface IEventService
{
    Task<EventDto?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResponse<EventDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default);
}
