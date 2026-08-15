using Microsoft.AspNetCore.Diagnostics;

namespace FunEvents.Api.Errors;

/// <summary>
/// Ultima red de seguridad: cualquier excepcion no prevista sale como 500 con
/// el mismo formato problem+json que el resto de errores.
/// </summary>
/// <remarks>
/// Nunca se filtra el mensaje de la excepcion al cliente: un stack trace o un
/// error de base de datos en el cuerpo de la respuesta es una fuga de
/// informacion. Se devuelve el <c>traceId</c> para que soporte pueda cruzarlo
/// con el log, que si tiene el detalle completo.
/// </remarks>
public sealed class UnhandledExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<UnhandledExceptionHandler> _logger;

    public UnhandledExceptionHandler(IProblemDetailsService problemDetails,
        ILogger<UnhandledExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Type = DomainErrorCatalog.TypeUriFor("INTERNAL_ERROR"),
                Title = "Internal Server Error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred. Quote the traceId when reporting it.",
                Instance = httpContext.Request.Path
            }
        });
    }
}
