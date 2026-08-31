using System.Text.Json.Serialization;
using FunEvents.Api.Errors;
using FunEvents.Api.Filters;
using FunEvents.Api.Middleware;
using FunEvents.Api.OpenApi;
using FunEvents.Api.Security;
using FunEvents.Application;
using FunEvents.Infrastructure;
using FunEvents.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging
// ---------------------------------------------------------------------------
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ---------------------------------------------------------------------------
// Servicios
// ---------------------------------------------------------------------------
builder.Services
    .AddControllers(options =>
    {
        // Sin este filtro, los validadores FluentValidation registrados mas
        // abajo nunca se ejecutarian (era el caso en la version anterior).
        options.Filters.Add<ValidationFilter>();
    })
    .AddJsonOptions(options =>
    {
        // Los enums viajan como texto ("Online", "Reserved"), no como enteros:
        // el contrato es legible y anadir un valor al enum no cambia el
        // significado de los que ya existen.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Autenticacion por API Key para el canal de colaboradores, autorizacion por
// scope y limitacion de peticiones particionada por colaborador. Ver
// SecurityServiceCollectionExtensions.
builder.Services.AddFunEventsSecurity(builder.Configuration);

// ProblemDetails (RFC 9457) como formato unico de error de toda la API. Ver
// ProblemDetailsServiceCollectionExtensions.
builder.Services.AddFunEventsProblemDetails();

// El orden importa: se evalua el primero registrado. El especifico antes que
// el generico, o el generico se tragaria todas las excepciones de dominio.
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();

// Swagger/OpenAPI, incluido el esquema de seguridad de la API Key. Ver
// SwaggerServiceCollectionExtensions.
builder.Services.AddFunEventsSwagger();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

builder.Services.AddResponseCaching();

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

// El correlation id va PRIMERO: asi el scope de log ya esta activo cuando el
// manejador de excepciones registra el fallo. En la version anterior el manejo
// de excepciones se montaba antes y los logs de error salian sin correlacion,
// justo los que mas falta hace poder rastrear.
app.UseCorrelationId();

// El log de peticiones va POR DELANTE del manejo de excepciones, y el orden
// entre estos dos importa mas de lo que parece.
//
// Al reves -que es como estaba-, la DomainException viajaba a traves del
// middleware de Serilog antes de que nadie la tradujera, asi que cada rechazo
// por regla de negocio se registraba como "responded 500" con su traza
// completa... y justo despues como Warning con el 409 o el 422 real. Dos lineas
// contradictorias por peticion, y un panel de errores lleno de incidencias que
// no lo son.
//
// Con este orden, el manejador de excepciones queda DENTRO del ambito de
// Serilog: la excepcion no llega a escapar, y lo que se registra es el codigo
// de estado que el cliente recibio de verdad.
app.UseSerilogRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Orden deliberado y no intercambiable:
//   1. UseAuthentication resuelve la API Key y deja la identidad en User.
//   2. UseApiKeyRejection corta con 401 una clave presente pero invalida, antes
//      de que consuma cupo de nadie.
//   3. UseRateLimiter necesita la identidad ya resuelta: sin ella, todos los
//      colaboradores compartirian la particion anonima de su IP de salida y el
//      limite por colaborador no existiria en la practica.
app.UseAuthentication();
app.UseApiKeyRejection();
app.UseRateLimiter();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FunEvents API v1");
    c.RoutePrefix = "swagger";
});

app.UseResponseCaching();

// Liveness: responde mientras el proceso este vivo. No consulta la base de
// datos a proposito; si lo hiciera, una caida de la base reiniciaria en bucle
// unos contenedores que estan perfectamente sanos.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: SI comprueba dependencias. Es la que debe mirar el balanceador
// para decidir si mandar trafico a esta instancia.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapControllers();

await app.RunAsync();

/// <summary>
/// Expuesto para que <c>WebApplicationFactory&lt;Program&gt;</c> pueda arrancar
/// la API en los tests de integracion.
/// </summary>
public partial class Program;
