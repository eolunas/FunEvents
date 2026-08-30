using FunEvents.Application.Availability;
using FunEvents.Application.Events;
using FunEvents.Application.Events.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace FunEvents.Api.Controllers;

/// <summary>Catalogo de eventos y disponibilidad.</summary>
/// <remarks>
/// Disponibilidad vive en este controlador y no en uno aparte porque
/// <c>/events/{id}/availability</c> es un sub-recurso del evento: separarlo
/// obligaba a dos clases con la misma <c>[Route("api/v1/events")]</c>, algo que
/// funciona pero desconcierta a quien busca donde esta definida una ruta.
/// </remarks>
[Route("api/v1/events")]
public class EventsController(IEventService events, IAvailabilityService availability) : ApiControllerBase
{
    /// <summary>Lista los eventos publicados, paginados.</summary>
    /// <param name="page">Pagina, empezando en 1.</param>
    /// <param name="pageSize">Tamano de pagina (maximo 100).</param>
    /// <param name="search">Filtro por nombre, sin distinguir mayusculas.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<EventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EventDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await events.GetPagedAsync(page, pageSize, search, ct));

    /// <summary>Detalle de un evento.</summary>
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDto>> GetById(Guid eventId, CancellationToken ct)
        => Respond(await events.GetByIdAsync(eventId, ct), "Event", eventId, "EVENT_NOT_FOUND");

    /// <summary>Disponibilidad actual de un evento.</summary>
    /// <remarks>
    /// Cacheable 5 segundos. Es una lectura muy frecuente (cada visitante del
    /// portal la pide) y tolerable de desactualizar unos segundos: el numero es
    /// orientativo, la verdad la decide el UPDATE atomico al reservar. Sin
    /// cache, un evento popular convierte esta ruta en el cuello de botella.
    /// </remarks>
    [HttpGet("{eventId:guid}/availability")]
    [ResponseCache(Duration = 5, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(AvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(Guid eventId, CancellationToken ct)
        => Respond(await availability.GetAvailabilityAsync(eventId, ct), "Event", eventId, "EVENT_NOT_FOUND");
}
