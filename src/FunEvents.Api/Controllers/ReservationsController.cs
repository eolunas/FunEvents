using System.ComponentModel.DataAnnotations;
using FunEvents.Api.Security;
using FunEvents.Application.Reservations;
using FunEvents.Application.Reservations.Dtos;
using FunEvents.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace FunEvents.Api.Controllers;

/// <summary>
/// Flujo de reserva. Es el endpoint que consume el cliente de consola y el que
/// integrarian los colaboradores en su propio portal o POS.
/// </summary>
/// <remarks>
/// <para>
/// <b>No hay un solo try/catch aqui.</b> Las violaciones de reglas de negocio
/// viajan como <c>DomainException</c> y las traduce a HTTP un unico manejador
/// (<c>DomainExceptionHandler</c> + <c>DomainErrorCatalog</c>). El controlador
/// se limita a lo que le corresponde: leer la peticion, resolver la identidad
/// del llamante, delegar y elegir entre 201 y 200.
/// </para>
/// <para>
/// <b>Por que la autorizacion no es un atributo.</b> Que una reserva exija
/// credencial de colaborador depende del <i>canal</i>, que viaja en el cuerpo,
/// no de la ruta. Un <c>[Authorize]</c> se evalua antes de enlazar el cuerpo,
/// asi que la regla se comprueba aqui, en el unico punto donde ya se conocen a
/// la vez la identidad y el canal.
/// </para>
/// </remarks>
[Route("api/v1/reservations")]
public class ReservationsController(IReservationService reservations) : ApiControllerBase
{
    /// <summary>Reserva entradas de un evento para un usuario.</summary>
    /// <param name="idempotencyKey">
    /// Identificador unico de intento, generado por el cliente. Reintentar con
    /// la misma key devuelve la reserva original (200) en lugar de crear otra.
    /// </param>
    /// <response code="201">Reserva creada.</response>
    /// <response code="200">Reproduccion de una reserva creada antes con esta misma key.</response>
    /// <response code="400">Peticion invalida o falta el header Idempotency-Key.</response>
    /// <response code="401">El canal Partner exige una API Key valida.</response>
    /// <response code="403">La API Key es valida pero no concede el permiso reservations:create.</response>
    /// <response code="404">El evento o el usuario no existen.</response>
    /// <response code="409">No queda aforo, u otra peticion con la misma key esta en curso.</response>
    /// <response code="422">Regla de negocio incumplida (evento no publicado, limite por usuario...).</response>
    /// <response code="429">Se ha superado el limite de peticiones. Ver el header Retry-After.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReservationResponse>> Create(
        [FromHeader(Name = "Idempotency-Key")][Required] string idempotencyKey,
        [FromBody] CreateReservationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Problem(
                StatusCodes.Status400BadRequest,
                "Missing Idempotency-Key header",
                "POST /api/v1/reservations requires a client-generated Idempotency-Key header.",
                "MISSING_IDEMPOTENCY_KEY");

        var effective = request;

        if (request.Channel == SalesChannel.Partner)
        {
            if (!User.IsPartner())
                return Problem(
                    StatusCodes.Status401Unauthorized,
                    "API key required",
                    "Reservations on the Partner channel require a valid X-Api-Key header.",
                    SecurityErrorCodes.ApiKeyRequired);

            if (!User.HasScope(ApiScopes.ReservationsCreate))
                return Problem(
                    StatusCodes.Status403Forbidden,
                    "Insufficient scope",
                    $"This API key does not grant the '{ApiScopes.ReservationsCreate}' scope.",
                    SecurityErrorCodes.InsufficientScope);

            // El colaborador se toma de la credencial, no del cuerpo. El
            // validador ya rechaza un PartnerId enviado por el cliente, asi que
            // este es el unico origen posible del valor que se persiste.
            effective = request with { PartnerId = User.GetPartnerId() };
        }

        var (reservation, replayed) = await reservations.CreateAsync(effective, idempotencyKey, ct);

        // 200 en la reproduccion y 201 solo en la creacion real: el codigo de
        // estado le dice al cliente si su reintento provoco algo nuevo.
        return RespondCreatedOrOk(
            created: !replayed,
            actionName: nameof(GetById),
            routeValues: new { reservationId = reservation.ReservationId },
            value: reservation);
    }

    /// <summary>Consulta una reserva por su identificador.</summary>
    /// <remarks>
    /// <b>Aislamiento entre colaboradores.</b> Si quien pregunta es un
    /// colaborador y la reserva no es suya, la respuesta es <c>404</c> y no
    /// <c>403</c>. Un 403 confirmaria que ese identificador existe, y la
    /// existencia de una reserva ajena ya es informacion que no le corresponde:
    /// con suficientes intentos, un 403 permite estimar el volumen de negocio
    /// de la competencia.
    /// </remarks>
    [HttpGet("{reservationId:guid}", Name = nameof(GetById))]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid reservationId, CancellationToken ct)
    {
        var reservation = await reservations.GetByIdAsync(reservationId, ct);

        return Respond(
            reservation, "Reservation", reservationId, "RESERVATION_NOT_FOUND",
            visible: reservation is not null && IsVisibleToCaller(reservation.PartnerId));
    }

    /// <summary>Consulta la URL de una reserva.</summary>
    /// <remarks>
    /// <b>Aislamiento entre colaboradores.</b> Mismo criterio que
    /// <see cref="GetById"/>: 404, nunca 403, si la reserva no pertenece a
    /// quien pregunta.
    /// </remarks>
    [HttpGet("url/{reservationId:guid}", Name = nameof(GetURLById))]
    [ProducesResponseType(typeof(ReservationUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationUrlResponse>> GetURLById(Guid reservationId, CancellationToken ct)
    {
        var reservationUrl = await reservations.GetUrlByIdAsync(reservationId, ct);

        return Respond(
            reservationUrl, "Reservation", reservationId, "RESERVATION_NOT_FOUND",
            visible: reservationUrl is not null && IsVisibleToCaller(reservationUrl.PartnerId));
    }

    private bool IsVisibleToCaller(Guid? partnerId) => !User.IsPartner() || partnerId == User.GetPartnerId();
}
