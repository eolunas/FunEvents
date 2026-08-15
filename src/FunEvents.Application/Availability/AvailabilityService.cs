using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Availability;

public class AvailabilityService : IAvailabilityService
{
    private readonly IEventRepository _events;
    private readonly TimeProvider _clock;

    public AvailabilityService(IEventRepository events, TimeProvider clock)
    {
        _events = events;
        _clock = clock;
    }

    public async Task<AvailabilityResponse?> GetAvailabilityAsync(Guid eventId, CancellationToken ct = default)
    {
        var @event = await _events.GetByIdAsync(eventId, ct);
        if (@event is null) return null;

        return new AvailabilityResponse
        {
            EventId = @event.Id,
            EventName = @event.Name,
            TotalCapacity = @event.Capacity,
            ReservedCount = @event.ReservedCount,
            AvailableCount = @event.AvailableCapacity(),
            IsOpenForSale = @event.IsOpenForSale(),
            AsOf = _clock.GetUtcNow()
        };
    }
}
