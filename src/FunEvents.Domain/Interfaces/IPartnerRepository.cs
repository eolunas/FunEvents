using FunEvents.Domain.Partners;

namespace FunEvents.Domain.Interfaces;

public interface IPartnerRepository
{
    Task<Partner?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default);
    Task<Partner?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
