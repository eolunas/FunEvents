using FunEvents.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace FunEvents.Api.Errors;

/// <summary>
/// Construye respuestas <c>application/problem+json</c> (RFC 9457) con la misma
/// forma que las que produce <see cref="DomainExceptionHandler"/>.
/// </summary>
/// <remarks>
/// Existe porque no todos los errores nacen de una excepcion de dominio: el
/// rechazo de una API Key y el 429 del limitador se generan en middleware, antes
/// de que exista un controlador. Sin este punto unico, un integrador se
/// encontraria con dos formatos de error distintos en la misma API segun donde
/// se hubiera cortado la peticion, que es exactamente lo que el contrato de
/// errores promete que no pasa.
/// </remarks>
public static class ApiProblem
{
    public static ProblemDetails Create(
        HttpContext context,
        int status,
        string title,
        string detail,
        string errorCode)
    {
        var problem = new ProblemDetails
        {
            Type = DomainErrorCatalog.TypeUriFor(errorCode),
            Title = title,
            Status = status,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;

        if (context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var correlationId))
            problem.Extensions["correlationId"] = correlationId;

        return problem;
    }

    /// <summary>Escribe el problema directamente en la respuesta. Para uso desde middleware.</summary>
    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string title,
        string detail,
        string errorCode,
        CancellationToken ct = default)
    {
        var problem = Create(context, status, title, detail, errorCode);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        // Sobrecarga posicional a proposito: WriteAsJsonAsync tiene varias y
        // pasar el token por nombre deja la resolucion mas expuesta de lo
        // necesario a un cambio de firma.
        await context.Response.WriteAsJsonAsync(problem, ct);
    }
}
