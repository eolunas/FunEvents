using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace FunEvents.IntegrationTests.Catalog;

[Collection(FunEventsApiCollection.Name)]
public class CatalogTests
{
    private static readonly Guid FunFest = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid DraftEvent = Guid.Parse("a0000000-0000-0000-0000-000000000004");

    private readonly HttpClient _client;

    public CatalogTests(FunEventsApiFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task El_catalogo_devuelve_los_eventos_sembrados()
    {
        var response = await _client.GetAsync("/api/v1/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        body.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task El_catalogo_no_expone_eventos_en_borrador()
    {
        var response = await _client.GetAsync("/api/v1/events?pageSize=100");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().NotContain(DraftEvent, "un evento en borrador no esta a la venta");
    }

    [Fact]
    public async Task La_busqueda_no_distingue_mayusculas()
    {
        // Regresion: antes se usaba Contains, que en PostgreSQL genera LIKE y
        // si distingue mayusculas. Buscar "funfest" no encontraba "FunFest 2026".
        var response = await _client.GetAsync("/api/v1/events?search=funfest");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task La_disponibilidad_refleja_el_aforo_del_evento()
    {
        var response = await _client.GetAsync($"/api/v1/events/{FunFest}/availability");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalCapacity").GetInt32().Should().Be(100);
        body.GetProperty("availableCount").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.GetProperty("isOpenForSale").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Un_evento_inexistente_devuelve_404()
    {
        var response = await _client.GetAsync($"/api/v1/events/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Los_usuarios_de_demostracion_estan_disponibles()
    {
        var response = await _client.GetAsync("/api/v1/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<JsonElement>();
        users.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Liveness_responde_sin_consultar_la_base_de_datos()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_comprueba_la_base_de_datos()
    {
        var response = await _client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Toda_respuesta_incluye_el_correlation_id()
    {
        var response = await _client.GetAsync("/api/v1/events");

        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
    }

    [Fact]
    public async Task El_correlation_id_del_cliente_se_respeta()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events");
        request.Headers.Add("X-Correlation-Id", "traza-de-prueba-123");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle()
            .Which.Should().Be("traza-de-prueba-123");
    }
}
