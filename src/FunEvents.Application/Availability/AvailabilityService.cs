using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Availability;

public class AvailabilityService(IEventRepository events, TimeProvider clock) : IAvailabilityService
{
    public async Task<AvailabilityResponse?> GetAvailabilityAsync(Guid eventId, CancellationToken ct = default)
    {
        var @event = await events.GetByIdAsync(eventId, ct);
        return @event?.ToAvailabilityResponse(clock);
    }
}
