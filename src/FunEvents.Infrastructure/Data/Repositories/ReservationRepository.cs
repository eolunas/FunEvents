using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data.Repositories;

public class ReservationRepository(AppDbContext db) : IReservationRepository
{
    public async Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(Reservation reservation, CancellationToken ct = default)
        => await db.Reservations.AddAsync(reservation, ct);

    public async Task<int> CountActiveTicketsAsync(Guid userId, Guid eventId, CancellationToken ct = default)
        => await db.Reservations
            .AsNoTracking()
            .Where(r => r.UserId == userId
                        && r.EventId == eventId
                        && (r.State == ReservationState.Reserved || r.State == ReservationState.Confirmed))
            .SumAsync(r => r.TicketQuantity, ct);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> es la razon de bajar a SQL en crudo aqui:
    /// no tiene equivalente en LINQ y sin el, N instancias de la API en paralelo
    /// leerian el mismo lote de reservas caducadas y devolverian el aforo N veces.
    /// Con SKIP LOCKED, cada instancia se lleva un lote disjunto y el worker es
    /// seguro en despliegues multi-replica sin necesidad de un lock distribuido.
    ///
    /// Debe ejecutarse dentro de una transaccion: los bloqueos de fila se
    /// mantienen hasta el commit.
    /// </remarks>
    public async Task<IReadOnlyList<Reservation>> ClaimExpiredAsync(int batchSize, CancellationToken ct = default)
        => await db.Reservations
            .FromSql($@"
                SELECT * FROM ""Reservations""
                WHERE ""State"" = 'Reserved' AND ""ExpiresAt"" < NOW()
                ORDER BY ""ExpiresAt""
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
}
