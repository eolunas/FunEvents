using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FunEvents.Infrastructure.BackgroundJobs;

/// <summary>
/// Esqueleto comun a los workers que sondean una fuente a intervalos fijos.
/// </summary>
/// <remarks>
/// Antes <see cref="IdempotencyCleanupWorker"/> y
/// <see cref="ReservationExpirationWorker"/> repetian, casi linea por linea, el
/// mismo <see cref="PeriodicTimer"/> con su try/catch de cancelacion y de
/// errores puntuales. Cada worker aporta solo lo que lo diferencia: el trabajo
/// de cada tick (<see cref="ExecuteTickAsync"/>) y sus mensajes de arranque y
/// parada (<see cref="OnStarting"/>, <see cref="OnStopped"/>).
/// </remarks>
public abstract class PeriodicBackgroundService(TimeSpan pollingInterval, ILogger logger) : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        OnStarting();

        using var timer = new PeriodicTimer(pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un fallo puntual (base de datos reiniciandose, deadlock) no debe
                // tumbar el worker: se registra y se reintenta en el siguiente tick.
                logger.LogError(ex, "{Worker} failed while processing a tick", GetType().Name);
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

        OnStopped();
    }

    /// <summary>Trabajo de un unico tick. Las excepciones se registran y no detienen el worker.</summary>
    protected abstract Task ExecuteTickAsync(CancellationToken ct);

    /// <summary>Se invoca una vez, antes del primer tick.</summary>
    protected virtual void OnStarting() { }

    /// <summary>Se invoca una vez, cuando el worker se detiene.</summary>
    protected virtual void OnStopped() { }
}
