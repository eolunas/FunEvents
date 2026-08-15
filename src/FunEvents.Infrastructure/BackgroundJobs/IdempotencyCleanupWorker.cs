using FunEvents.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public class IdempotencyCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;
    private readonly IdempotencyCleanupOptions _options;

    public IdempotencyCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyCleanupWorker> logger,
        IOptions<IdempotencyCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IdempotencyCleanupWorker started (retention {Retention}h, interval {Interval}h)",
            _options.Retention.TotalHours, _options.PollingInterval.TotalHours);

        using var timer = new PeriodicTimer(_options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();

                var cutoff = DateTimeOffset.UtcNow - _options.Retention;
                var purged = await store.PurgeOlderThanAsync(cutoff, stoppingToken);

                if (purged > 0)
                    _logger.LogInformation("Purged {Count} idempotency key(s) older than {Cutoff:u}",
                        purged, cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error purging idempotency keys");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("IdempotencyCleanupWorker stopped");
    }
}
