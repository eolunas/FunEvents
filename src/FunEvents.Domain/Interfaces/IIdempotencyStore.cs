namespace FunEvents.Domain.Interfaces;

/// <summary>
/// Registro de peticiones idempotentes. El almacen es quien garantiza la
/// exclusion mutua entre reintentos concurrentes de la misma Idempotency-Key
/// (en la implementacion actual, mediante la clave primaria de la tabla).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Devuelve el registro de la key, tanto si esta completado como si sigue
    /// en curso. <c>null</c> si la key nunca se ha visto.
    /// </summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Intenta tomar la key en exclusiva. <c>false</c> si ya la tomo otra
    /// peticion.
    /// </summary>
    /// <param name="requestFingerprint">
    /// Hash del cuerpo de la peticion. Permite detectar el caso peligroso de
    /// reutilizar una key con un payload distinto.
    /// </param>
    Task<bool> TryBeginAsync(string key, string requestFingerprint, CancellationToken ct = default);

    /// <summary>Marca la key como completada y guarda la respuesta a reproducir.</summary>
    Task CompleteAsync(string key, Guid reservationId, int statusCode, string responseBody,
        CancellationToken ct = default);

    /// <summary>
    /// Libera una key tomada cuya operacion fallo, para que el cliente pueda
    /// reintentar con la misma key. Sin esto, un fallo transitorio dejaria la
    /// key envenenada para siempre.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken ct = default);

    /// <summary>Purga keys mas antiguas que <paramref name="olderThan"/>.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}

public sealed class IdempotencyRecord
{
    public string Key { get; init; } = string.Empty;
    public Guid? ReservationId { get; init; }
    public string? ResponseBody { get; init; }
    public int? StatusCode { get; init; }
    public string RequestFingerprint { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>La peticion original termino y hay una respuesta que reproducir.</summary>
    public bool IsCompleted => CompletedAt is not null && !string.IsNullOrEmpty(ResponseBody);
}
