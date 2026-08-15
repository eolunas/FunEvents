using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace FunEvents.IntegrationTests;

/// <summary>
/// Levanta la API completa contra un PostgreSQL real en un contenedor efimero.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que PostgreSQL de verdad y no el proveedor InMemory de EF.</b> Todo
/// lo que hace interesante a este sistema depende del motor: el UPDATE
/// condicional atomico que impide la sobreventa, la violacion de clave primaria
/// que sirve de exclusion mutua para la Idempotency-Key, <c>FOR UPDATE SKIP
/// LOCKED</c> en la caducidad y los indices parciales. El proveedor InMemory no
/// implementa nada de eso: los tests pasarian en verde y no probarian
/// absolutamente nada de lo que importa.
/// </para>
/// <para><b>Requiere Docker en ejecucion.</b> Sin Docker, esta suite falla al
/// arrancar; los tests unitarios no la necesitan.</para>
/// </remarks>
public class FunEventsApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // La imagen se fija en el constructor: desde Testcontainers 4.13 el
    // constructor sin parametros esta obsoleto, precisamente para que ningun
    // proyecto acabe probando contra una version de PostgreSQL que nadie eligio.
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("funevents_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        Client = CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString());

        // Limite alto a proposito: las pruebas de concurrencia lanzan muchas
        // reservas reutilizando pocos usuarios y no queremos que el limite por
        // usuario enmascare lo que se esta midiendo (el control de aforo).
        builder.UseSetting("ReservationPolicy:MaxTicketsPerUserPerEvent", "1000");
        builder.UseSetting("ReservationPolicy:HoldWindow", "00:30:00");

        // El limitador se desactiva en la suite compartida: la prueba de
        // concurrencia lanza 15 peticiones simultaneas desde la misma "IP" y el
        // limitador anonimo las contaria juntas, midiendo el limitador en vez
        // del control de aforo. El limitador se prueba aparte, sobre su funcion
        // de particionado, que es donde vive la decision.
        builder.UseSetting("Security:RateLimiting:Enabled", "false");

        // Los workers en segundo plano se dejan practicamente dormidos para que
        // no caduquen reservas ni purguen keys en mitad de una asercion.
        builder.UseSetting("ReservationExpiration:PollingInterval", "01:00:00");
        builder.UseSetting("IdempotencyCleanup:PollingInterval", "01:00:00");
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }
}

/// <summary>
/// Un unico contenedor de PostgreSQL compartido por todas las clases de test.
/// Arrancar uno por clase multiplica el tiempo de la suite sin aportar nada.
/// </summary>
[CollectionDefinition(Name)]
public class FunEventsApiCollection : ICollectionFixture<FunEventsApiFixture>
{
    public const string Name = "FunEvents API";
}
