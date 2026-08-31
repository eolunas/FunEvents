using FunEvents.Application.Users.Dtos;
using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Users;

public class UserService(IUserRepository users) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
        => (await users.GetAllAsync(ct)).Select(u => u.ToDto()).ToList();

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(id, ct);
        return user?.ToDto();
    }
}
