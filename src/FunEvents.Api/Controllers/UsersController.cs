using FunEvents.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace FunEvents.Api.Controllers;

/// <summary>
/// Consulta de usuarios de demostracion.
/// </summary>
/// <remarks>
/// El enunciado parte de "codigos de evento y de usuario ya conocidos". Este
/// endpoint existe para que el cliente de consola pueda descubrir esos codigos
/// sin que haya que copiarlos a mano de un fichero de seed. En un sistema real
/// no seria un endpoint publico.
/// </remarks>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>Lista los usuarios registrados.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
        => Ok(await _users.GetAllAsync(ct));

    /// <summary>Detalle de un usuario.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid userId, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);

        return user is null
            ? NotFound(new ProblemDetails
            {
                Type = "https://api.funevents.com/errors/user-not-found",
                Title = "User not found",
                Status = StatusCodes.Status404NotFound,
                Detail = $"User {userId} does not exist.",
                Instance = HttpContext.Request.Path
            })
            : Ok(user);
    }
}
