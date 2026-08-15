using FunEvents.Application.Reservations;
using FunEvents.Domain.Interfaces;
using FunEvents.Infrastructure.BackgroundJobs;
using FunEvents.Infrastructure.Data;
using FunEvents.Infrastructure.Data.Repositories;
using FunEvents.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FunEvents.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set ConnectionStrings__DefaultConnection or add it to appsettings.json.");

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);

                // NOTA: EnableRetryOnFailure esta DESACTIVADO a proposito.
                //
                // La estrategia de reintentos de Npgsql solo puede reintentar
                // una unidad de trabajo completa, y reintentar una que ya anadio
                // entidades al change tracker duplicaria esos INSERT. Combinarla
                // con transacciones explicitas exige rehidratar el DbContext en
                // cada intento, y para el alcance de este prototipo eso complica
                // mas de lo que aporta.
                //
                // UnitOfWork ya pasa por CreateExecutionStrategy(), asi que
                // activarla es cambiar esta linea, no reescribir el flujo.
                // Ver ADR-010.
            });
        });

        // --- Persistencia ---
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        // --- Politicas configurables ---
        services.Configure<ReservationPolicyOptions>(
            configuration.GetSection(ReservationPolicyOptions.SectionName));
        services.Configure<ReservationExpirationOptions>(
            configuration.GetSection(ReservationExpirationOptions.SectionName));
        services.Configure<IdempotencyCleanupOptions>(
            configuration.GetSection(IdempotencyCleanupOptions.SectionName));

        // --- Procesos en segundo plano ---
        services.AddHostedService<ReservationExpirationWorker>();
        services.AddHostedService<IdempotencyCleanupWorker>();

        return services;
    }
}
