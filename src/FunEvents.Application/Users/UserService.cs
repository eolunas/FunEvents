using FunEvents.Domain.Interfaces;

namespace FunEvents.Application.Users;

public record UserDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Consulta de usuarios. Solo lectura: el alta de usuarios queda fuera del
/// alcance de la prueba, que parte de codigos de usuario ya conocidos.
/// </summary>
public class UserService(IUserRepository users) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default)
        => (await users.GetAllAsync(ct)).Select(Map).ToList();

    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(id, ct);
        return user is null ? null : Map(user);
    }

    private static UserDto Map(Domain.Users.User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        IsActive = user.IsActive
    };
}
