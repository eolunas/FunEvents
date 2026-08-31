using FunEvents.Domain.Partners;

namespace FunEvents.Domain.Interfaces;

public interface IPartnerRepository
{
    /// <summary>
    /// Colaborador activo cuya clave (ya hasheada por el llamante) coincide.
    /// <see langword="null"/> si no existe o esta desactivado.
    /// </summary>
    Task<Partner?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default);

    Task<Partner?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
