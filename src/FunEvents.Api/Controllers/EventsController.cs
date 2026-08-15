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
[ApiController]
[Route("api/v1/events")]
[Produces("application/json")]
public class EventsController : ControllerBase
{
    private readonly IEventService _events;
    private readonly IAvailabilityService _availability;

    public EventsController(IEventService events, IAvailabilityService availability)
    {
        _events = events;
        _availability = availability;
    }

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
        => Ok(await _events.GetPagedAsync(page, pageSize, search, ct));

    /// <summary>Detalle de un evento.</summary>
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType(typeof(EventDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDto>> GetById(Guid eventId, CancellationToken ct)
    {
        var @event = await _events.GetByIdAsync(eventId, ct);
        return @event is null ? EventNotFound(eventId) : Ok(@event);
    }

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
    {
        var availability = await _availability.GetAvailabilityAsync(eventId, ct);
        return availability is null ? EventNotFound(eventId) : Ok(availability);
    }

    private NotFoundObjectResult EventNotFound(Guid eventId) => NotFound(new ProblemDetails
    {
        Type = "https://api.funevents.com/errors/event-not-found",
        Title = "Event not found",
        Status = StatusCodes.Status404NotFound,
        Detail = $"Event {eventId} does not exist.",
        Instance = HttpContext.Request.Path
    });
}
