using FunEvents.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace FunEvents.Api.Errors;

/// <summary>
/// Convierte una violacion de regla de negocio en una respuesta
/// <c>application/problem+json</c> (RFC 9457).
/// </summary>
/// <remarks>
/// Usa <see cref="IExceptionHandler"/>, el mecanismo introducido en .NET 8,
/// en lugar de un middleware artesanal: se integra con
/// <c>IProblemDetailsService</c>, respeta las opciones de serializacion de la
/// aplicacion y permite encadenar varios manejadores por orden de registro.
/// </remarks>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false; // No es cosa nuestra: pasa al siguiente manejador.

        var (status, title, type) = DomainErrorCatalog.Resolve(domainException);

        // Warning, no Error: una regla de negocio que rechaza una peticion es
        // funcionamiento normal del sistema, no un fallo. Registrarlo como Error
        // hace que los paneles de alertas se llenen de ruido y se dejen de mirar.
        logger.LogWarning(
            "Domain rule rejected {Method} {Path}: {ErrorCode} - {Message}",
            httpContext.Request.Method, httpContext.Request.Path,
            domainException.ErrorCode, domainException.Message);

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = domainException,
            ProblemDetails =
            {
                Type = type,
                Title = title,
                Status = status,
                Detail = domainException.Message,
                Instance = httpContext.Request.Path,
                Extensions =
                {
                    // El codigo estable es lo que un cliente debe programar,
                    // no el texto de Detail ni la URL de Type.
                    ["errorCode"] = domainException.ErrorCode
                }
            }
        });
    }
}
