using FluentAssertions;
using FunEvents.Api.Security;

namespace FunEvents.IntegrationTests.Security;

/// <summary>
/// Pruebas de la funcion que decide en que cubo cuenta cada peticion.
/// </summary>
/// <remarks>
/// <para>
/// <b>No necesitan Docker ni levantar la API</b>: viven en este proyecto porque
/// es el unico de pruebas que referencia la capa Api, no porque necesiten
/// infraestructura. La regla que verifican —quien comparte cupo con quien— es
/// la unica parte del limitador que puede estar mal de forma sutil: el resto lo
/// implementa el framework.
/// </para>
/// <para>
/// Probar el limitador de punta a punta exigiria emitir cientos de peticiones
/// reales y esperar a que se reponga la ventana, lo que convierte una suite
/// determinista en una que falla los viernes. Aqui se prueba la decision; que
/// <c>FixedWindowRateLimiter</c> cuenta bien es responsabilidad de .NET.
/// </para>
/// </remarks>
public class RateLimitPartitionResolverTests
{
    private static readonly RateLimitingOptions Options = new()
    {
        Enabled = true,
        Window = TimeSpan.FromMinutes(1),
        AnonymousPermitLimit = 300
    };

    private static readonly Guid PartnerA = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid PartnerB = Guid.Parse("c0000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/swagger/index.html")]
    public void Las_rutas_de_salud_y_documentacion_no_se_limitan(string path)
    {
        var decision = Resolve(path, isPartner: false, partnerId: null, permitLimit: null);

        decision.Limited.Should().BeFalse(
            "limitar el sondeo del orquestador convierte un pico de trafico en un reinicio en bucle");
    }

    [Fact]
    public void Cada_colaborador_tiene_su_propia_particion()
    {
        var a = Resolve("/api/v1/events", isPartner: true, partnerId: PartnerA, permitLimit: 500);
        var b = Resolve("/api/v1/events", isPartner: true, partnerId: PartnerB, permitLimit: 500);

        a.PartitionKey.Should().NotBe(b.PartitionKey,
            "un socio con un bucle mal programado no puede consumir el cupo de los demas");
    }

    [Fact]
    public void El_cupo_del_colaborador_sale_de_su_contrato_y_no_del_limite_anonimo()
    {
        var decision = Resolve("/api/v1/events", isPartner: true, partnerId: PartnerA, permitLimit: 60);

        decision.Limited.Should().BeTrue();
        decision.PermitLimit.Should().Be(60);
    }

    [Fact]
    public void Un_colaborador_sin_cupo_configurado_cae_al_limite_por_defecto()
    {
        var decision = Resolve("/api/v1/events", isPartner: true, partnerId: PartnerA, permitLimit: 0);

        decision.PermitLimit.Should().Be(Options.AnonymousPermitLimit);
    }

    [Fact]
    public void El_trafico_sin_credencial_se_agrupa_por_origen()
    {
        var first = Resolve("/api/v1/events", isPartner: false, partnerId: null, permitLimit: null, ip: "10.0.0.1");
        var second = Resolve("/api/v1/events", isPartner: false, partnerId: null, permitLimit: null, ip: "10.0.0.2");

        first.PartitionKey.Should().NotBe(second.PartitionKey);
        first.PermitLimit.Should().Be(Options.AnonymousPermitLimit);
    }

    /// <summary>
    /// Sin origen conocido —una peticion sin IP remota, que ocurre detras de
    /// algunos proxies— todas caen en la misma particion. Es deliberado: es
    /// preferible que ese trafico comparta cupo a que quede sin limite alguno.
    /// </summary>
    [Fact]
    public void Sin_origen_conocido_el_trafico_sigue_estando_limitado()
    {
        var decision = Resolve("/api/v1/events", isPartner: false, partnerId: null, permitLimit: null, ip: null);

        decision.Limited.Should().BeTrue();
        decision.PartitionKey.Should().Be("anon:unknown");
    }

    [Fact]
    public void Con_el_limitador_desactivado_nada_se_limita()
    {
        var disabled = new RateLimitingOptions { Enabled = false };

        var decision = RateLimitPartitionResolver.Resolve(
            "/api/v1/reservations", isPartner: true, PartnerA, 10, "10.0.0.1", disabled);

        decision.Limited.Should().BeFalse();
    }

    private static RateLimitDecision Resolve(
        string path, bool isPartner, Guid? partnerId, int? permitLimit, string? ip = "10.0.0.1")
        => RateLimitPartitionResolver.Resolve(path, isPartner, partnerId, permitLimit, ip, Options);
}
