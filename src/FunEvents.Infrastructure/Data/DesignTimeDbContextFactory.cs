using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FunEvents.Infrastructure.Data;

/// <summary>
/// Permite que las herramientas <c>dotnet ef</c> construyan el
/// <see cref="AppDbContext"/> sin arrancar la aplicacion completa.
/// </summary>
/// <remarks>
/// Sin esto, <c>dotnet ef migrations add</c> intenta levantar el host de la API
/// (y por tanto conectarse a la base de datos y ejecutar el inicializador) solo
/// para leer el modelo. La cadena de conexion de aqui es de diseno: solo se usa
/// para que el proveedor sepa que dialecto generar; nunca se abre.
/// Se puede sobreescribir con la variable de entorno FUNEVENTS_DESIGNTIME_CONNECTION.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnection =
        "Host=localhost;Port=5432;Database=funevents;Username=funevents;Password=funevents";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FUNEVENTS_DESIGNTIME_CONNECTION") ?? FallbackConnection;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
