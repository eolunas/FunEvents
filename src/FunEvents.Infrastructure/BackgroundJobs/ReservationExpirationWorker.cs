using FunEvents.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunEvents.Infrastructure.BackgroundJobs;

public sealed class ReservationExpirationOptions
{
    public const string SectionName = "ReservationExpiration";

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int BatchSize { get; set; } = 200;
}

/// <summary>
/// Devuelve al aforo las plazas de las reservas que caducaron sin confirmarse.
/// </summary>
/// <remarks>
/// <para>
/// Es seguro con varias instancias de la API en ejecucion: el repositorio toma
/// el lote con <c>FOR UPDATE SKIP LOCKED</c>, asi que dos replicas nunca
/// procesan la misma reserva y el aforo no se devuelve dos veces.
/// </para>
/// <para>
/// Marcar la reserva como Expired y devolver su cupo ocurre dentro de la misma
/// transaccion. Si el proceso muere a mitad, el rollback deja las reservas como
/// Reserved y la siguiente pasada las vuelve a tomar.
/// </para>
/// </remarks>
public class ReservationExpirationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ReservationExpirationWorker> logger,
    IOptions<ReservationExpirationOptions> options)
    : PeriodicBackgroundService(options.Value.PollingInterval, logger)
{
    protected override async Task ExecuteTickAsync(CancellationToken ct)
    {
        try
        {
            var released = await ProcessBatchAsync(ct);
            if (released > 0)
                logger.LogInformation("Expired {Count} reservation(s)", released);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un fallo puntual (base de datos reiniciandose, deadlock) no debe
            // tumbar el worker: se registra y se reintenta en el siguiente tick.
            logger.LogError(ex, "Error processing reservation expirations");
        }
    }

    protected override void OnStarting()
        => logger.LogInformation(
            "ReservationExpirationWorker started (interval {Interval}s, batch {BatchSize})",
            options.Value.PollingInterval.TotalSeconds, options.Value.BatchSize);

    protected override void OnStopped()
        => logger.LogInformation("ReservationExpirationWorker stopped");

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        // Un scope nuevo por pasada: el DbContext es scoped y no debe compartirse
        // entre iteraciones de un servicio singleton.
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reservations = scope.ServiceProvider.GetRequiredService<IReservationRepository>();
        var events = scope.ServiceProvider.GetRequiredService<IEventRepository>();

        return await unitOfWork.ExecuteInTransactionAsync<int>(async token =>
        {
            var expired = await reservations.ClaimExpiredAsync(options.Value.BatchSize, token);
            if (expired.Count == 0) return 0;

            foreach (var reservation in expired)
            {
                reservation.MarkExpired();

                var released = await events.ReleaseCapacityAsync(
                    reservation.EventId, reservation.TicketQuantity, token);

                if (!released)
                {
                    // No deberia ocurrir: significaria que el contador del evento
                    // esta por debajo de lo que esta reserva retiene. Se registra
                    // en vez de silenciarlo, porque indica corrupcion de datos.
                    logger.LogError(
                        "Could not release {Quantity} ticket(s) for event {EventId} " +
                        "while expiring reservation {ReservationId}: counter is inconsistent",
                        reservation.TicketQuantity, reservation.EventId, reservation.Id);
                }

                logger.LogDebug("Expired reservation {ReservationId} ({Quantity} tickets)",
                    reservation.Id, reservation.TicketQuantity);
            }

            await unitOfWork.SaveChangesAsync(token);
            return expired.Count;
        }, ct);
    }
}
