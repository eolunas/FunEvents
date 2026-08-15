namespace FunEvents.Application.Availability;

public interface IAvailabilityService
{
    Task<AvailabilityResponse?> GetAvailabilityAsync(Guid eventId, CancellationToken ct = default);
}
