using FunEvents.Domain.Users;

namespace FunEvents.Domain.Interfaces;

public interface IUserRepository
{
    /// <summary><see langword="null"/> si el usuario no existe.</summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Comprobacion de existencia sin materializar la entidad.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Todos los usuarios.
    /// </summary>
    /// <remarks>
    /// Solo para la demo: el enunciado parte de "codigos de usuario ya
    /// conocidos" y el cliente de consola necesita poder mostrarlos. En un
    /// sistema real este listado seria paginado y estaria detras de un permiso
    /// administrativo; no se expone al canal publico.
    /// </remarks>
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
}
