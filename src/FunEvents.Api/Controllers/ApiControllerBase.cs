using FunEvents.Api.Errors;
using Microsoft.AspNetCore.Mvc;

namespace FunEvents.Api.Controllers;

/// <summary>
/// Base comun para los controllers de la API.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que existe.</b> Antes de esto, "recurso no encontrado" se construia
/// de tres formas distintas: <c>ReservationsController</c> pasaba por
/// <see cref="ApiProblem"/> (con <c>errorCode</c>, <c>traceId</c> y
/// <c>correlationId</c>), mientras que <c>EventsController</c> y
/// <c>UsersController</c> instanciaban un <see cref="ProblemDetails"/> a mano,
/// sin esos tres campos y sin forzar <c>application/problem+json</c>. El
/// comentario de <see cref="ApiProblem"/> promete un unico formato de error en
/// toda la API; en la practica solo lo cumplia un controller de tres.
/// </para>
/// <para>
/// <b>[ApiController] y [Produces] se declaran aqui y no en cada controller.</b>
/// <see cref="AttributeUsageAttribute"/> hereda por defecto (<c>Inherited =
/// true</c>) salvo que el atributo diga lo contrario, y ninguno de los dos lo
/// hace: repetirlos en cada clase derivada no cambiaba nada, solo anadia
/// ruido. <c>[Route]</c> si se queda en cada controller porque cada uno tiene
/// un prefijo distinto.
/// </para>
/// </remarks>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Respuesta <c>application/problem+json</c> (RFC 9457) con el mismo
    /// formato que usa <see cref="DomainExceptionHandler"/> para cualquier
    /// otro error de la API.
    /// </summary>
    protected ObjectResult Problem(int status, string title, string detail, string errorCode)
        => new(ApiProblem.Create(HttpContext, status, title, detail, errorCode))
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };

    /// <summary>404 con el mensaje estandar "{resourceName} {id} does not exist."</summary>
    protected ObjectResult NotFoundProblem(string resourceName, object id, string errorCode)
        => Problem(
            StatusCodes.Status404NotFound,
            $"{resourceName} not found",
            $"{resourceName} {id} does not exist.",
            errorCode);

    /// <summary>
    /// Patron "consultar por id": 200 con <paramref name="value"/> si existe y
    /// es visible para quien pregunta, 404 (mismo formato en toda la API) si
    /// no. <paramref name="visible"/> solo hace falta pasarlo cuando, ademas
    /// de existir, hay que comprobar algo mas -el aislamiento entre
    /// colaboradores en <c>ReservationsController</c>-; se omite en el resto.
    /// </summary>
    protected ActionResult<T> Respond<T>(
        T? value, string resourceName, object id, string errorCode, bool visible = true)
        where T : class
        => value is not null && visible
            ? Ok(value)
            : NotFoundProblem(resourceName, id, errorCode);

    /// <summary>
    /// Patron "crear o reproducir": 201 con <c>Location</c> si
    /// <paramref name="created"/> es <see langword="true"/>, 200 si la
    /// respuesta es la reproduccion de una peticion anterior (idempotencia).
    /// </summary>
    protected ActionResult<T> RespondCreatedOrOk<T>(
        bool created, string actionName, object routeValues, T value)
        => created
            ? CreatedAtAction(actionName, routeValues, value)
            : Ok(value);
}
