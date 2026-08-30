using Serilog.Context;

namespace FunEvents.Api.Middleware;

/// <summary>
/// Propaga un identificador de correlacion por toda la peticion.
/// </summary>
/// <remarks>
/// Si el cliente envia <c>X-Correlation-Id</c> se respeta; si no, se genera uno.
/// El valor se devuelve siempre en la respuesta y se adjunta al contexto de log,
/// de modo que todas las lineas de una misma peticion (incluidos los errores)
/// comparten el mismo identificador. Es lo que permite reconstruir que paso en
/// una reserva concreta cuando un colaborador reporta una incidencia.
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
            correlationId = Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;

        // OnStarting y no una escritura directa: las cabeceras solo se pueden
        // modificar mientras la respuesta no haya empezado a enviarse.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(ItemKey, correlationId))
        {
            await next(context);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        => builder.UseMiddleware<CorrelationIdMiddleware>();
}
