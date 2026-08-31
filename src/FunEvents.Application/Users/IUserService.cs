using FunEvents.Application.Users.Dtos;

namespace FunEvents.Application.Users;

/// <summary>
/// Consulta de usuarios. Solo lectura: el alta de usuarios queda fuera del
/// alcance de la prueba, que parte de codigos de usuario ya conocidos.
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary><see langword="null"/> si el usuario no existe.</summary>
    Task<UserDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
}
