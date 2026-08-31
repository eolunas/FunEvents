using FunEvents.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunEvents.Infrastructure.BackgroundJobs;

public sealed class IdempotencyCleanupOptions
{
    public const string SectionName = "IdempotencyCleanup";

    /// <summary>Cuanto tiempo se conserva una key antes de poder reutilizarla.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Purga las Idempotency-Keys vencidas.
/// </summary>
/// <remarks>
/// Existe porque la documentacion de arquitectura afirmaba que "las keys
/// expiran a las 24h (job de limpieza periodica)" y ese job no estaba escrito.
/// Sin el, la tabla crece de forma indefinida: una fila por cada peticion POST
/// jamas borrada. Se implementa en vez de retirar la afirmacion del documento,
/// porque el coste es bajo y la necesidad es real.
/// </remarks>
public class IdempotencyCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IdempotencyCleanupWorker> logger,
    IOptions<IdempotencyCleanupOptions> options)
    : PeriodicBackgroundService(options.Value.PollingInterval, logger)
{
    protected override async Task ExecuteTickAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

            var cutoff = DateTimeOffset.UtcNow - options.Value.Retention;
            var purged = await store.PurgeOlderThanAsync(cutoff, ct);

            if (purged > 0)
                logger.LogInformation("Purged {Count} idempotency key(s) older than {Cutoff:u}", purged, cutoff);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error purging idempotency keys");
        }
    }

    protected override void OnStarting()
        => logger.LogInformation(
            "IdempotencyCleanupWorker started (retention {Retention}h, interval {Interval}h)",
            options.Value.Retention.TotalHours, options.Value.PollingInterval.TotalHours);

    protected override void OnStopped()
        => logger.LogInformation("IdempotencyCleanupWorker stopped");
}
