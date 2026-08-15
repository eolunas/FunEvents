namespace FunEvents.Infrastructure.Idempotency;

/// <summary>
/// Fila de la tabla que soporta la idempotencia.
/// </summary>
/// <remarks>
/// Vive en Infrastructure y no en Domain a proposito: es un detalle del
/// mecanismo de entrega HTTP (reintentos de red), no una regla de negocio.
/// El dominio solo conoce la abstraccion <c>IIdempotencyStore</c>.
/// </remarks>
public class IdempotencyKey
{
    /// <summary>Valor del header <c>Idempotency-Key</c>. Clave primaria.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 del cuerpo de la peticion. Permite detectar el caso peligroso de
    /// reutilizar una key con un payload distinto, que de otro modo devolveria
    /// silenciosamente la respuesta de una reserva que no es la pedida.
    /// </summary>
    public string RequestFingerprint { get; set; } = string.Empty;

    public Guid? ReservationId { get; set; }
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// <c>null</c> mientras la peticion original sigue en curso.
    /// Es lo que distingue "en proceso" de "terminada y reproducible".
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
