using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Availability;

public class AvailabilityService(IEventRepository events, TimeProvider clock) : IAvailabilityService
{
    public async Task<AvailabilityResponse?> GetAvailabilityAsync(Guid eventId, CancellationToken ct = default)
    {
        var @event = await events.GetByIdAsync(eventId, ct);
        if (@event is null) return null;

        return new AvailabilityResponse
        {
            EventId = @event.Id,
            EventName = @event.Name,
            TotalCapacity = @event.Capacity,
            ReservedCount = @event.ReservedCount,
            AvailableCount = @event.AvailableCapacity(),
            IsOpenForSale = @event.IsOpenForSale(),
            AsOf = clock.GetUtcNow()
        };
    }
}
