using FunEvents.Application.Users;
using FunEvents.Application.Users.Dtos;
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
[Route("api/v1/users")]
public class UsersController(IUserService users) : ApiControllerBase
{
    /// <summary>Lista los usuarios registrados.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken ct)
        => Ok(await users.GetAllAsync(ct));

    /// <summary>Detalle de un usuario.</summary>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid userId, CancellationToken ct)
        => Respond(await users.GetByIdAsync(userId, ct), "User", userId, "USER_NOT_FOUND");
}
