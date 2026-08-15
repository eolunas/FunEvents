using FunEvents.Domain.Interfaces;
using FunEvents.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FunEvents.Infrastructure.Idempotency;

/// <summary>
/// Implementacion de <see cref="IIdempotencyStore"/> sobre PostgreSQL.
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<IdempotencyStore> _logger;

    public IdempotencyStore(AppDbContext db, ILogger<IdempotencyStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
    {
        // AsNoTracking: es una lectura de consulta, no queremos que la entidad
        // quede adherida al change tracker del scope de la peticion.
        var entity = await _db.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(ik => ik.Key == key, ct);

        return entity is null ? null : Map(entity);
    }

    public async Task<bool> TryBeginAsync(string key, string requestFingerprint, CancellationToken ct = default)
    {
        var entity = new IdempotencyKey
        {
            Key = key,
            RequestFingerprint = requestFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = null
        };

        _db.IdempotencyKeys.Add(entity);

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Otra peticion gano la carrera por la clave primaria.
            //
            // Hay que DESADHERIR la entidad fallida: si se deja en estado Added,
            // el proximo SaveChangesAsync de este mismo scope reintentaria el
            // INSERT duplicado y haria fallar una operacion que no tiene nada
            // que ver. La version anterior se tragaba la excepcion y dejaba el
            // contexto envenenado.
            _db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    public async Task CompleteAsync(string key, Guid reservationId, int statusCode, string responseBody,
        CancellationToken ct = default)
    {
        var entity = await _db.IdempotencyKeys.FirstOrDefaultAsync(ik => ik.Key == key, ct);
        if (entity is null)
        {
            _logger.LogWarning("Idempotency key {Key} disappeared before completion", key);
            return;
        }

        entity.ReservationId = reservationId;
        entity.StatusCode = statusCode;
        entity.ResponseBody = responseBody;
        entity.CompletedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        // ExecuteDelete: sentencia unica, sin cargar la entidad ni depender del
        // change tracker (que puede estar en un estado sucio tras el fallo que
        // nos trajo hasta aqui).
        var deleted = await _db.IdempotencyKeys
            .Where(ik => ik.Key == key && ik.CompletedAt == null)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation("Released idempotency key {Key} after a failed attempt", key);
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset olderThan, CancellationToken ct = default)
        => await _db.IdempotencyKeys
            .Where(ik => ik.CreatedAt < olderThan)
            .ExecuteDeleteAsync(ct);

    private static IdempotencyRecord Map(IdempotencyKey entity) => new()
    {
        Key = entity.Key,
        ReservationId = entity.ReservationId,
        ResponseBody = entity.ResponseBody,
        StatusCode = entity.StatusCode,
        RequestFingerprint = entity.RequestFingerprint,
        CreatedAt = entity.CreatedAt,
        CompletedAt = entity.CompletedAt
    };
}
