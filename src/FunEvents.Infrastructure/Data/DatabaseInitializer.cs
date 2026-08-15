using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FunEvents.Infrastructure.Data;

/// <summary>
/// Deja la base de datos lista al arrancar la API.
/// </summary>
/// <remarks>
/// <para>
/// La version anterior llamaba directamente a <c>MigrateAsync()</c> pero el
/// repositorio no contenia ninguna migracion. Resultado: la API arrancaba sin
/// error aparente, no creaba ni una tabla, y la primera peticion devolvia
/// 500 con "relation Events does not exist". Era el motivo principal de que el
/// prototipo no funcionase de punta a punta.
/// </para>
/// <para>
/// Ahora se cubren los dos casos de forma explicita:
/// si hay migraciones se aplican; si no las hay (clon recien hecho sobre el que
/// todavia no se ejecuto <c>dotnet ef migrations add</c>), se crea el esquema
/// desde el modelo y se avisa por log. El prototipo arranca siempre.
/// </para>
/// <para>
/// En produccion esto NO se hace al arrancar la API: se ejecuta como paso del
/// pipeline de despliegue, para que N replicas arrancando a la vez no compitan
/// por migrar y para poder revisar el SQL antes de aplicarlo. Ver architecture.md.
/// </para>
/// </remarks>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseInitializer));

        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var allMigrations = db.Database.GetMigrations().ToList();

        if (allMigrations.Count > 0)
        {
            if (pendingMigrations.Count == 0)
            {
                logger.LogInformation("Database schema is up to date ({Count} migrations applied)",
                    allMigrations.Count);
                return;
            }

            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}",
                pendingMigrations.Count, string.Join(", ", pendingMigrations));
            await db.Database.MigrateAsync(ct);
            return;
        }

        logger.LogWarning(
            "No EF Core migrations found in the assembly. Creating the schema directly from the model " +
            "(EnsureCreated). This is fine for the prototype, but before going to production run: " +
            "dotnet ef migrations add InitialCreate --project src/FunEvents.Infrastructure --startup-project src/FunEvents.Api");

        await db.Database.EnsureCreatedAsync(ct);
    }
}
