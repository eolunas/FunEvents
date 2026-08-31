using FunEvents.Domain.Events;

namespace FunEvents.Application.Availability;

public static class AvailabilityMappingExtensions
{
    public static AvailabilityResponse ToAvailabilityResponse(this Event e, TimeProvider clock) => new()
    {
        EventId = e.Id,
        EventName = e.Name,
        TotalCapacity = e.Capacity,
        ReservedCount = e.ReservedCount,
        AvailableCount = e.AvailableCapacity(),
        IsOpenForSale = e.IsOpenForSale(),
        AsOf = clock.GetUtcNow()
    };
}
