using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data.Repositories;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => await db.Users.AsNoTracking().AnyAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default)
        => await db.Users.AsNoTracking().OrderBy(u => u.FullName).ToListAsync(ct);
}
