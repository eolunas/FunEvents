using FunEvents.Api.Middleware;

namespace FunEvents.Api.Errors;

/// <summary>
/// Registro de ProblemDetails (RFC 9457) como formato unico de error de toda la API.
/// </summary>
public static class ProblemDetailsServiceCollectionExtensions
{
    public static IServiceCollection AddFunEventsProblemDetails(this IServiceCollection services)
        => services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] =
                    System.Diagnostics.Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

                if (context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
            };
        });
}
