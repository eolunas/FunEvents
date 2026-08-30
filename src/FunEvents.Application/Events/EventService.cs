using FunEvents.Application.Events.Dtos;
using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Events;

public class EventService(IEventRepository events) : IEventService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<EventDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var @event = await events.GetByIdAsync(id, ct);
        return @event is null ? null : MapToDto(@event);
    }

    public async Task<PagedResponse<EventDto>> GetPagedAsync(
        int page, int pageSize, string? search = null, CancellationToken ct = default)
    {
        // Se saneen aqui, en el caso de uso, y no en el controlador: cualquier
        // canal que consuma este servicio obtiene los mismos limites sin tener
        // que replicarlos.
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > MaxPageSize) pageSize = DefaultPageSize;

        var (items, totalCount) = await events.GetPagedAsync(page, pageSize, search, ct: ct);

        return new PagedResponse<EventDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private static EventDto MapToDto(Domain.Events.Event e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        Venue = e.Venue,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Capacity = e.Capacity,
        ReservedCount = e.ReservedCount,
        AvailableCapacity = e.AvailableCapacity(),
        State = e.State.ToString()
    };
}
