using FunEvents.Domain.Common;

namespace FunEvents.Domain.Partners;

/// <summary>
/// Colaborador que integra FunEvents en su propio portal o POS.
/// </summary>
/// <remarks>
/// El modelo esta definido y persistido, pero la <b>autenticacion por API Key
/// no esta implementada en este prototipo</b>: no hay middleware que valide
/// <c>X-Api-Key</c> ni filtrado por partner en las consultas. Se deja
/// deliberadamente fuera del alcance (ver README, seccion "Que NO esta
/// implementado") en lugar de simularlo a medias. El diseno completo esta en
/// architecture.md, ADR-006.
/// </remarks>
public class Partner : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>SHA-256 de la API Key. La key en claro solo existe en el momento de emitirla.</summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Permisos concedidos al colaborador. Se persiste como <c>text[]</c> nativo
    /// de PostgreSQL, no como una cadena separada por comas: evita tener que
    /// declarar un ValueConverter con su ValueComparer y permite consultar con
    /// operadores de array si algun dia hace falta.
    /// </summary>
    public List<string> Scopes { get; private set; } = new();

    /// <summary>Peticiones por minuto permitidas al colaborador.</summary>
    public int RateLimit { get; private set; }

    public static IReadOnlyList<string> DefaultScopes { get; } = new[]
    {
        "events:read",
        "reservations:create",
        "reservations:read"
    };

    // Requerido por EF Core para materializar desde la base de datos.
    private Partner() { }

    public Partner(string name, string apiKeyHash, IEnumerable<string>? scopes = null, int rateLimit = 500)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Partner name is required.", PartnerErrors.InvalidName);
        if (string.IsNullOrWhiteSpace(apiKeyHash))
            throw new DomainException("API key hash is required.", PartnerErrors.InvalidKey);
        if (rateLimit <= 0)
            throw new DomainException("Rate limit must be greater than zero.", PartnerErrors.InvalidRateLimit);

        Name = name;
        ApiKeyHash = apiKeyHash;
        IsActive = true;
        Scopes = (scopes ?? DefaultScopes).ToList();
        RateLimit = rateLimit;
    }

    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.Ordinal);

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }
}

public static class PartnerErrors
{
    public const string InvalidName = "PARTNER_INVALID_NAME";
    public const string InvalidKey = "PARTNER_INVALID_KEY";
    public const string InvalidRateLimit = "PARTNER_INVALID_RATE_LIMIT";
}
