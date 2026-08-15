using FluentValidation;
using FunEvents.Domain.Common;
using Microsoft.Extensions.Options;

namespace FunEvents.Application.Reservations.Dtos;

/// <summary>
/// Peticion de reserva.
/// </summary>
/// <remarks>
/// <b>UserId es nuevo y no es cosmetico.</b> El enunciado pide reservar
/// "a partir de un codigo de evento y de usuario ya conocidos", y la version
/// anterior solo aceptaba el evento: la reserva se guardaba con
/// <c>UserId = null</c> y el canal fijado a la cadena "user". Sin el usuario no
/// se puede saber quien compro, ni limitar entradas por persona, ni distinguir
/// una venta de oficina de una del portal.
/// </remarks>
public record CreateReservationRequest
{
    /// <summary>Codigo del evento.</summary>
    public Guid EventId { get; init; }

    /// <summary>Codigo del comprador.</summary>
    public Guid UserId { get; init; }

    /// <summary>Numero de entradas.</summary>
    public int TicketQuantity { get; init; }

    /// <summary>
    /// Canal de venta. Por defecto <see cref="SalesChannel.Online"/>, que es el
    /// canal principal segun el enunciado.
    /// </summary>
    public SalesChannel Channel { get; init; } = SalesChannel.Online;

    /// <summary>
    /// Colaborador que origina la venta.
    /// </summary>
    /// <remarks>
    /// <b>No se acepta en el cuerpo de la peticion.</b> Lo fija el servidor a
    /// partir de la API Key presentada. Aceptarlo del cliente permitiria a un
    /// colaborador atribuir sus ventas a otro con solo cambiar un campo del
    /// JSON: la identidad nunca puede venir del mismo sitio que los datos.
    /// La propiedad existe porque la capa de aplicacion si necesita recibirla
    /// —el controlador construye la peticion efectiva con el valor derivado de
    /// la credencial— y porque entra en la huella de idempotencia.
    /// </remarks>
    public Guid? PartnerId { get; init; }
}

public class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator(IOptions<ReservationPolicyOptions> policy)
    {
        var limits = policy.Value;

        RuleFor(x => x.EventId)
            .NotEmpty().WithMessage("EventId is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required.");

        RuleFor(x => x.TicketQuantity)
            .GreaterThan(0).WithMessage("TicketQuantity must be greater than 0.")
            .LessThanOrEqualTo(limits.MaxTicketsPerRequest)
            .WithMessage($"A single reservation cannot exceed {limits.MaxTicketsPerRequest} tickets.");

        RuleFor(x => x.Channel)
            .IsInEnum().WithMessage("Channel must be one of: Online, Office, Partner.");

        // PartnerId NO se acepta del cliente en ningun canal: para el canal
        // Partner lo deriva el servidor de la API Key, y para el resto no
        // aplica. Rechazarlo explicitamente con un 400 es mejor que ignorarlo
        // en silencio: un integrador que lo envia esta asumiendo algo falso
        // sobre el contrato y conviene que se entere en la primera llamada.
        RuleFor(x => x.PartnerId)
            .Empty()
            .WithMessage("PartnerId is not accepted in the request body; it is derived from the API key.");
    }
}
