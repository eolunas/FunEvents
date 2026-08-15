using System.Threading.RateLimiting;
using FunEvents.Api.Errors;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FunEvents.Api.Security;

/// <summary>
/// Registro de autenticacion por API Key y de la limitacion de peticiones.
/// Agrupado aqui para que <c>Program.cs</c> siga leyendose como un indice del
/// arranque y no como el sitio donde vive la configuracion.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddFunEventsSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.AddMemoryCache();

        services
            .AddAuthentication(ApiKeyDefaults.Scheme)
            .AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });

        // No se declaran politicas con nombre a proposito. La regla de esta API
        // -"reservar por el canal Partner exige credencial y el permiso
        // reservations:create"- depende del cuerpo de la peticion, no de la
        // ruta, y una politica se evalua antes de enlazar el cuerpo. Declarar
        // politicas que despues nadie aplica seria peor que no tenerlas: da a
        // entender que la autorizacion se resuelve en un sitio donde no ocurre.
        services.AddAuthorization();

        services.AddRateLimiter(ConfigureRateLimiter);

        return services;
    }

    private static void ConfigureRateLimiter(RateLimiterOptions limiter)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var options = context.RequestServices
                .GetRequiredService<IOptions<SecurityOptions>>().Value.RateLimiting;

            var decision = RateLimitPartitionResolver.Resolve(
                path: context.Request.Path.Value ?? "/",
                isPartner: context.User.IsPartner(),
                partnerId: context.User.GetPartnerId(),
                partnerPermitLimit: context.User.GetRateLimit(),
                clientIdentifier: context.Connection.RemoteIpAddress?.ToString(),
                options: options);

            if (!decision.Limited)
                return RateLimitPartition.GetNoLimiter(decision.PartitionKey);

            return RateLimitPartition.GetFixedWindowLimiter(decision.PartitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = decision.PermitLimit,
                    Window = options.Window,
                    QueueLimit = options.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        });

        limiter.OnRejected = async (context, ct) =>
        {
            var options = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<SecurityOptions>>().Value.RateLimiting;

            // Retry-After no es cortesia: es la diferencia entre un cliente que
            // espera lo justo y uno que reintenta en bucle y agrava el pico que
            // provoco el rechazo.
            var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
                ? metadata
                : options.Window;

            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

            await ApiProblem.WriteAsync(
                context.HttpContext,
                StatusCodes.Status429TooManyRequests,
                "Rate limit exceeded",
                "Too many requests. Check the Retry-After header before retrying.",
                SecurityErrorCodes.RateLimitExceeded,
                ct);
        };
    }
}
