namespace FunEvents.Application.Availability;

public interface IAvailabilityService
{
    /// <summary>Aforo restante de un evento. <see langword="null"/> si el evento no existe.</summary>
    Task<AvailabilityResponse?> GetAvailabilityAsync(Guid eventId, CancellationToken ct = default);
}
