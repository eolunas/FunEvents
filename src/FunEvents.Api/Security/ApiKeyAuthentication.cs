using System.Security.Claims;
using System.Text.Encodings.Web;
using FunEvents.Domain.Interfaces;
using FunEvents.Domain.Partners;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FunEvents.Api.Security;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";

    /// <summary>Tipo de claim en el que viajan los permisos del colaborador.</summary>
    public const string ScopeClaimType = "scope";

    /// <summary>Tipo de claim en el que viaja el limite de peticiones del colaborador.</summary>
    public const string RateLimitClaimType = "rate_limit";
}

/// <summary>Opciones del esquema. No anade nada; existe porque el tipo base lo exige.</summary>
public class ApiKeySchemeOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Autentica al colaborador a partir de la cabecera <c>X-Api-Key</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que API Key y no solo JWT.</b> Un JWT emitido es valido hasta que
/// caduca: revocarlo exige una lista de revocacion consultada en cada peticion,
/// que es justo la consulta que el JWT pretendia evitar. Un colaborador
/// comercial puede dejar de serlo de un dia para otro — impago, fin de
/// contrato, filtracion de la clave — y en ese momento la propiedad que importa
/// es poder cortarle el acceso ya. Con API Key, revocar es un UPDATE.
/// </para>
/// <para>
/// <b>Por que no devuelve 401 aqui.</b> Este manejador se limita a resolver la
/// identidad: si no hay cabecera devuelve <c>NoResult</c> (peticion anonima, que
/// sigue siendo legitima para el catalogo publico) y si la cabecera es invalida
/// devuelve <c>Fail</c>. Quien traduce ese fallo a un 401 es
/// <see cref="ApiKeyRejectionMiddleware"/>. Separarlo permite que una misma
/// peticion anonima pase por el catalogo y sea rechazada en reservas de canal
/// Partner, sin dos esquemas de autenticacion distintos.
/// </para>
/// <para>
/// <b>La clave en claro no se registra en ningun log.</b> Cuando el rechazo se
/// registra, se hace con los primeros caracteres del hash, nunca con la clave.
/// </para>
/// </remarks>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeySchemeOptions>
{
    private readonly IMemoryCache _cache;
    private readonly SecurityOptions _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeySchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IMemoryCache cache,
        IOptions<SecurityOptions> security)
        : base(options, logger, encoder)
    {
        _cache = cache;
        _security = security.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerName = _security.ApiKey.HeaderName;

        if (!Request.Headers.TryGetValue(headerName, out var values))
            return AuthenticateResult.NoResult();

        var apiKey = values.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.Fail("Empty API key.");

        var hash = ApiKeyHasher.Hash(apiKey);

        var partner = await ResolvePartnerAsync(hash);
        if (partner is null)
        {
            // Se registra el prefijo del HASH, nunca la clave: un log es un
            // artefacto que se copia, se exporta y se comparte.
            Logger.LogWarning("Rejected API key with hash prefix {HashPrefix}", hash[..8]);
            return AuthenticateResult.Fail("Unknown or inactive API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, partner.Id.ToString()),
            new(ClaimTypes.Name, partner.Name),
            new(ApiKeyDefaults.RateLimitClaimType, partner.RateLimit.ToString())
        };

        claims.AddRange(partner.Scopes.Select(scope => new Claim(ApiKeyDefaults.ScopeClaimType, scope)));

        var identity = new ClaimsIdentity(claims, ApiKeyDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
    }

    /// <summary>
    /// Resuelve el colaborador desde la cache y, si no esta, desde la base de datos.
    /// </summary>
    /// <remarks>
    /// El repositorio se resuelve del contenedor de la peticion en vez de
    /// inyectarse en el constructor: los manejadores de autenticacion los
    /// construye el framework y mezclar su ciclo de vida con el de un
    /// <c>DbContext</c> con ambito de peticion es una fuente conocida de
    /// <c>ObjectDisposedException</c> intermitentes.
    /// </remarks>
    private async Task<Partner?> ResolvePartnerAsync(string hash)
    {
        var cacheKey = $"apikey:{hash}";

        if (_cache.TryGetValue<Partner>(cacheKey, out var cached))
            return cached;

        var repository = Context.RequestServices.GetRequiredService<IPartnerRepository>();
        var partner = await repository.GetByApiKeyHashAsync(hash, Context.RequestAborted);

        // Se cachea tambien el resultado negativo, con una ventana mas corta:
        // sin esto, un cliente mal configurado que reintenta en bucle con una
        // clave invalida genera una consulta a la base de datos por peticion,
        // que es un vector de denegacion de servicio barato.
        _cache.Set(
            cacheKey,
            partner,
            partner is null
                ? TimeSpan.FromSeconds(5)
                : _security.ApiKey.CacheDuration);

        return partner;
    }
}

/// <summary>Lectura tipada de la identidad del colaborador.</summary>
public static class PartnerPrincipalExtensions
{
    public static bool IsPartner(this ClaimsPrincipal principal)
        => principal.Identity?.IsAuthenticated == true
           && principal.Identity.AuthenticationType == ApiKeyDefaults.Scheme;

    public static Guid? GetPartnerId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public static bool HasScope(this ClaimsPrincipal principal, string scope)
        => principal.FindAll(ApiKeyDefaults.ScopeClaimType)
            .Any(claim => string.Equals(claim.Value, scope, StringComparison.Ordinal));

    public static int? GetRateLimit(this ClaimsPrincipal principal)
        => int.TryParse(principal.FindFirstValue(ApiKeyDefaults.RateLimitClaimType), out var limit)
            ? limit
            : null;
}

/// <summary>Permisos que reconoce la API.</summary>
public static class ApiScopes
{
    public const string EventsRead = "events:read";
    public const string ReservationsCreate = "reservations:create";
    public const string ReservationsRead = "reservations:read";
}
