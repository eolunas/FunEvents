using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Partners;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data.Repositories;

public class PartnerRepository : IPartnerRepository
{
    private readonly AppDbContext _db;

    public PartnerRepository(AppDbContext db) => _db = db;

    public async Task<Partner?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default)
        => await _db.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ApiKeyHash == apiKeyHash && p.IsActive, ct);

    public async Task<Partner?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Partners.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
}
