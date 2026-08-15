using FunEvents.Domain.Events;
using FunEvents.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data.Repositories;

public class EventRepository : IEventRepository
{
    private readonly AppDbContext _db;

    public EventRepository(AppDbContext db) => _db = db;

    /// <remarks>
    /// AsNoTracking deliberado. El aforo NUNCA se modifica cargando la entidad
    /// y guardando (eso reintroduciria la carrera lectura-escritura que el
    /// UPDATE condicional elimina), asi que mantener el evento en el change
    /// tracker solo sirve para que quede desincronizado despues de
    /// <see cref="TryReserveCapacityAsync"/>.
    /// </remarks>
    public async Task<Event?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(IReadOnlyList<Event> Items, int TotalCount)> GetPagedAsync(
        int page, int pageSize, string? search = null, Guid? partnerId = null, CancellationToken ct = default)
    {
        var query = _db.Events.AsNoTracking().Where(e => e.State == EventState.Published);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILike en vez de Contains: en PostgreSQL, Contains genera LIKE, que
            // distingue mayusculas de minusculas. Buscar "funfest" no encontraba
            // "FunFest 2026".
            var pattern = $"%{search.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.Name, pattern));
        }

        if (partnerId.HasValue)
            query = query.Where(e => e.PartnerId == null || e.PartnerId == partnerId.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Id)   // desempate estable: sin esto la paginacion puede repetir u omitir filas
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task AddAsync(Event @event, CancellationToken ct = default)
        => await _db.Events.AddAsync(@event, ct);

    /// <inheritdoc/>
    public async Task<bool> TryReserveCapacityAsync(Guid eventId, int quantity, CancellationToken ct = default)
    {
        // Se traduce a:
        //   UPDATE "Events"
        //   SET "ReservedCount" = "ReservedCount" + @q, "UpdatedAt" = @now
        //   WHERE "Id" = @id AND "State" = 'Published' AND "Capacity" - "ReservedCount" >= @q
        //
        // La condicion se evalua en el motor bajo el row lock del UPDATE, de modo
        // que entre comprobar la disponibilidad y consumirla no hay ninguna
        // ventana en la que otra transaccion pueda colarse. 0 filas afectadas
        // significa "no cabia", y es la unica respuesta posible: nunca puede
        // producir sobreventa.
        var rowsAffected = await _db.Events
            .Where(e => e.Id == eventId
                        && e.State == EventState.Published
                        && e.Capacity - e.ReservedCount >= quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.ReservedCount, e => e.ReservedCount + quantity)
                .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return rowsAffected == 1;
    }

    /// <inheritdoc/>
    public async Task<bool> ReleaseCapacityAsync(Guid eventId, int quantity, CancellationToken ct = default)
    {
        // La guarda ReservedCount >= quantity evita que un doble procesamiento
        // deje el contador en negativo. Antes no existia: la caducidad podia
        // restar por debajo de cero y "crear" aforo inexistente.
        var rowsAffected = await _db.Events
            .Where(e => e.Id == eventId && e.ReservedCount >= quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.ReservedCount, e => e.ReservedCount - quantity)
                .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow), ct);

        return rowsAffected == 1;
    }
}
