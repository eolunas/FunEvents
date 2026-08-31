using FunEvents.Api.Security;
// Microsoft.OpenApi 2.x colapso el namespace Microsoft.OpenApi.Models dentro de
// Microsoft.OpenApi. Como Microsoft.AspNetCore.OpenApi 10.x arrastra la version 2.x,
// el using antiguo (Microsoft.OpenApi.Models) ya no resuelve.
using Microsoft.OpenApi;

namespace FunEvents.Api.OpenApi;

/// <summary>
/// Registro de Swagger/OpenAPI, incluido el esquema de seguridad de la API Key.
/// </summary>
public static class SwaggerServiceCollectionExtensions
{
    public static IServiceCollection AddFunEventsSwagger(this IServiceCollection services)
        => services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "FunEvents API",
                Version = "v1",
                Description =
                    "API de venta y reserva de entradas multicanal (portal propio, oficinas y colaboradores).\n\n" +
                    "POST /api/v1/reservations exige el header Idempotency-Key: reintentar con la misma key " +
                    "devuelve la reserva original en lugar de crear una segunda.\n\n" +
                    "El canal Partner exige ademas el header X-Api-Key. El catalogo es publico."
            });

            // Declarar el esquema en OpenAPI no es cosmetico: es lo que permite a un
            // colaborador generar un cliente que ya sabe donde va la credencial, y lo
            // que habilita el boton "Authorize" de esta misma pagina.
            options.AddSecurityDefinition(ApiKeyDefaults.Scheme, new OpenApiSecurityScheme
            {
                Name = "X-Api-Key",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Clave del colaborador. Solo es necesaria para el canal Partner."
            });
        });
}
