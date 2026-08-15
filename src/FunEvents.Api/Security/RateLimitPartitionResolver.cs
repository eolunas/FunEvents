namespace FunEvents.Api.Security;

/// <summary>
/// Decision de limitacion para una peticion: en que cubo cuenta y cuantas
/// peticiones caben en ese cubo.
/// </summary>
public readonly record struct RateLimitDecision(string PartitionKey, int PermitLimit, bool Limited)
{
    public static RateLimitDecision Exempt(string key) => new(key, 0, Limited: false);
}

/// <summary>
/// Elige la particion del limitador. Es una funcion pura, sin
/// <c>HttpContext</c>, precisamente para poder probarla sin levantar la API:
/// la regla de "quien comparte cupo con quien" es logica de negocio de la
/// integracion, no un detalle de infraestructura.
/// </summary>
public static class RateLimitPartitionResolver
{
    /// <summary>
    /// Rutas exentas. <c>/health</c> lo consulta el orquestador cada pocos
    /// segundos: limitarlo significaria que, bajo un pico de trafico, Kubernetes
    /// recibe 429 en el sondeo, da la instancia por caida y la reinicia —
    /// convirtiendo un pico en una caida. <c>/swagger</c> es documentacion.
    /// </summary>
    private static readonly string[] ExemptPrefixes = ["/health", "/swagger"];

    public static RateLimitDecision Resolve(
        string path,
        bool isPartner,
        Guid? partnerId,
        int? partnerPermitLimit,
        string? clientIdentifier,
        RateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
            return RateLimitDecision.Exempt("disabled");

        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return RateLimitDecision.Exempt("exempt");
        }

        // Colaborador identificado: cuota propia, tomada de su contrato.
        // Que el cupo sea por colaborador y no global es lo que impide que un
        // socio con un bucle mal programado consuma el cupo de los demas.
        if (isPartner && partnerId is { } id)
        {
            var permit = partnerPermitLimit is > 0 ? partnerPermitLimit.Value : options.AnonymousPermitLimit;
            return new RateLimitDecision($"partner:{id}", permit, Limited: true);
        }

        // Trafico sin credencial: se agrupa por origen. Es una aproximacion
        // reconocida — detras de un NAT muchos usuarios comparten IP — y por eso
        // el limite anonimo es holgado. La proteccion fina la da la credencial.
        var origin = string.IsNullOrWhiteSpace(clientIdentifier) ? "unknown" : clientIdentifier;
        return new RateLimitDecision($"anon:{origin}", options.AnonymousPermitLimit, Limited: true);
    }
}
