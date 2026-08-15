namespace FunEvents.Domain.Reservations;

/// <summary>
/// Codigos de error estables del flujo de reserva.
/// La API mapea estos codigos a status HTTP; el texto del mensaje es para
/// humanos y puede cambiar sin romper a los clientes.
/// </summary>
public static class ReservationErrors
{
    public const string InsufficientCapacity = "INSUFFICIENT_CAPACITY";
    public const string EventNotFound = "EVENT_NOT_FOUND";
    public const string EventNotPublished = "EVENT_NOT_PUBLISHED";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserInactive = "USER_INACTIVE";
    public const string PerUserLimitExceeded = "PER_USER_LIMIT_EXCEEDED";
    public const string InvalidQuantity = "INVALID_QUANTITY";
    public const string InvalidEvent = "INVALID_EVENT";
    public const string InvalidUser = "INVALID_USER";
    public const string InvalidPartner = "INVALID_PARTNER";
    public const string AlreadyExpired = "ALREADY_EXPIRED";
    public const string NotReserved = "NOT_RESERVED";
    public const string ReservationNotFound = "RESERVATION_NOT_FOUND";

    /// <summary>Otra peticion con la misma Idempotency-Key sigue en curso.</summary>
    public const string RequestInProgress = "REQUEST_IN_PROGRESS";

    /// <summary>La Idempotency-Key ya se uso con un cuerpo de peticion distinto.</summary>
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
}
