using FunEvents.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data;

/// <inheritdoc cref="IUnitOfWork"/>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation, CancellationToken ct = default)
    {
        // Si ya hay una transaccion abierta (por ejemplo, un caso de uso que
        // compone a otro), nos unimos a ella en lugar de anidar. PostgreSQL no
        // tiene transacciones anidadas reales y abrir una segunda lanzaria.
        if (_db.Database.CurrentTransaction is not null)
            return await operation(ct);

        // CreateExecutionStrategy es obligatorio aqui, no opcional:
        // con EnableRetryOnFailure activo, llamar directamente a
        // BeginTransactionAsync lanza InvalidOperationException
        // ("the configured execution strategy does not support user-initiated
        // transactions"). Con la politica de reintentos desactivada devuelve
        // una estrategia de paso directo, asi que este codigo es correcto en
        // ambas configuraciones.
        var strategy = _db.Database.CreateExecutionStrategy();

        // El argumento generico va explicito: una lambda async encaja tanto en
        // la sobrecarga Func<CancellationToken, Task> como en la
        // Func<CancellationToken, Task<TResult>>, y sin el la resolucion de
        // sobrecarga es ambigua.
        return await strategy.ExecuteAsync<TResult>(async token =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(token);
            try
            {
                var result = await operation(token);
                await transaction.CommitAsync(token);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(token);
                throw;
            }
        }, ct);
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> operation, CancellationToken ct = default)
        => await ExecuteInTransactionAsync<object?>(async token =>
        {
            await operation(token);
            return null;
        }, ct);
}
