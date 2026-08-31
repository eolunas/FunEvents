using FunEvents.Domain.Common;
using FunEvents.Domain.Events;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;

namespace FunEvents.Api.Errors;

/// <summary>
/// Unico punto donde un codigo de error de dominio se traduce a un status HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Antes esta traduccion vivia como una cadena de <c>catch (DomainException ex)
/// when (ex.ErrorCode == ...)</c> dentro del controlador de reservas. Eso tenia
/// tres problemas: el controlador ocupaba 70 lineas de las que 55 eran manejo
/// de errores, cada endpoint nuevo tenia que repetir la cadena, y era facil
/// olvidar un codigo y devolver 500 por una regla de negocio perfectamente
/// prevista.
/// </para>
/// <para>
/// Con un catalogo, anadir una regla de negocio es anadir una entrada aqui.
/// Y lo que no este mapeado degrada a 422 (regla de negocio incumplida),
/// nunca a 500.
/// </para>
/// </remarks>
public static class DomainErrorCatalog
{
    private const string TypeBaseUri = "https://api.funevents.com/errors/";

    private static readonly IReadOnlyDictionary<string, (int Status, string Title)> Map =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            // --- 401/403: fallos de autenticacion/autorizacion del canal Partner ---
            [ReservationErrors.PartnerCredentialRequired] = (StatusCodes.Status401Unauthorized, "API key required"),
            [ReservationErrors.InsufficientScope] = (StatusCodes.Status403Forbidden, "Insufficient scope"),

            // --- 404: el recurso referenciado no existe ---
            [ReservationErrors.EventNotFound] = (StatusCodes.Status404NotFound, "Event not found"),
            [ReservationErrors.UserNotFound] = (StatusCodes.Status404NotFound, "User not found"),
            [UserErrors.NotFound] = (StatusCodes.Status404NotFound, "User not found"),
            [ReservationErrors.ReservationNotFound] = (StatusCodes.Status404NotFound, "Reservation not found"),

            // --- 409: conflicto con el estado actual del recurso; reintentar puede funcionar ---
            [ReservationErrors.InsufficientCapacity] = (StatusCodes.Status409Conflict, "Insufficient capacity"),
            [ReservationErrors.RequestInProgress] = (StatusCodes.Status409Conflict, "Request already in progress"),

            // --- 422: la peticion es sintacticamente valida pero viola una regla ---
            [ReservationErrors.EventNotPublished] = (StatusCodes.Status422UnprocessableEntity, "Event is not open for sale"),
            [ReservationErrors.UserInactive] = (StatusCodes.Status422UnprocessableEntity, "User is not active"),
            [UserErrors.Inactive] = (StatusCodes.Status422UnprocessableEntity, "User is not active"),
            [ReservationErrors.PerUserLimitExceeded] = (StatusCodes.Status422UnprocessableEntity, "Per-user ticket limit exceeded"),
            [ReservationErrors.IdempotencyKeyReused] = (StatusCodes.Status422UnprocessableEntity, "Idempotency-Key reused with a different body"),
            [ReservationErrors.AlreadyExpired] = (StatusCodes.Status422UnprocessableEntity, "Reservation already expired"),
            [ReservationErrors.NotReserved] = (StatusCodes.Status422UnprocessableEntity, "Invalid reservation state transition"),
            [EventErrors.InvalidTransition] = (StatusCodes.Status422UnprocessableEntity, "Invalid event state transition"),

            // --- 400: la peticion esta mal formada ---
            [ReservationErrors.InvalidQuantity] = (StatusCodes.Status400BadRequest, "Invalid ticket quantity"),
            [ReservationErrors.InvalidEvent] = (StatusCodes.Status400BadRequest, "Invalid event"),
            [ReservationErrors.InvalidUser] = (StatusCodes.Status400BadRequest, "Invalid user"),
            [ReservationErrors.InvalidPartner] = (StatusCodes.Status400BadRequest, "Invalid partner"),
        };

    public static (int Status, string Title, string Type) Resolve(DomainException exception)
    {
        var (status, title) = Map.TryGetValue(exception.ErrorCode, out var mapped)
            ? mapped
            : (StatusCodes.Status422UnprocessableEntity, "Business rule violation");

        return (status, title, TypeUriFor(exception.ErrorCode));
    }

    /// <summary>INSUFFICIENT_CAPACITY -> https://api.funevents.com/errors/insufficient-capacity</summary>
    public static string TypeUriFor(string errorCode)
        => TypeBaseUri + errorCode.ToLowerInvariant().Replace('_', '-');
}
