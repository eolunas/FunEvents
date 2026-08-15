using FunEvents.Api.Errors;
using Microsoft.Extensions.Options;

namespace FunEvents.Api.Security;

/// <summary>
/// Corta con <c>401</c> las peticiones que presentan una API Key que no resuelve
/// a ningun colaborador activo.
/// </summary>
/// <remarks>
/// <para>
/// La regla es "credencial ausente es anonimo; credencial presente tiene que ser
/// valida". Sin este middleware, una clave caducada se comportaria como trafico
/// anonimo: el colaborador recibiria 200 en el catalogo y un 401 solo al
/// reservar, y tardaria en darse cuenta de que su clave dejo de funcionar. Un
/// fallo de credenciales tiene que ser inmediato y explicito.
/// </para>
/// <para>
/// Se coloca justo detras de <c>UseAuthentication</c> y por delante del
/// limitador: una clave invalida no debe consumir el cupo del colaborador
/// legitimo cuyo identificador todavia no conocemos.
/// </para>
/// </remarks>
public class ApiKeyRejectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _headerName;

    public ApiKeyRejectionMiddleware(RequestDelegate next, IOptions<SecurityOptions> security)
    {
        _next = next;
        _headerName = security.Value.ApiKey.HeaderName;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var presented = context.Request.Headers.ContainsKey(_headerName);

        if (presented && !context.User.IsPartner())
        {
            await ApiProblem.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Invalid API key",
                $"The {_headerName} header does not match any active partner.",
                SecurityErrorCodes.InvalidApiKey,
                context.RequestAborted);

            return;
        }

        await _next(context);
    }
}

public static class SecurityErrorCodes
{
    public const string InvalidApiKey = "INVALID_API_KEY";
    public const string ApiKeyRequired = "API_KEY_REQUIRED";
    public const string InsufficientScope = "INSUFFICIENT_SCOPE";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
}

public static class ApiKeyRejectionMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyRejection(this IApplicationBuilder app)
        => app.UseMiddleware<ApiKeyRejectionMiddleware>();
}
