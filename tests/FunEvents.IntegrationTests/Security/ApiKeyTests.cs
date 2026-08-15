using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace FunEvents.IntegrationTests.Security;

/// <summary>
/// Autenticacion por API Key, permisos y aislamiento entre colaboradores,
/// contra la API real y PostgreSQL real.
/// </summary>
/// <remarks>
/// Es la suite que impide que la seguridad se degrade en silencio. Un fallo de
/// autorizacion no rompe ninguna funcionalidad visible: la API sigue
/// respondiendo 201 y la demo sigue pasando. Sin estas aserciones, el dia que
/// alguien reordene el pipeline y deje <c>UseAuthentication</c> detras del
/// controlador, nada avisaria.
/// </remarks>
[Collection(FunEventsApiCollection.Name)]
public class ApiKeyTests
{
    // Datos sembrados (src/FunEvents.Infrastructure/Data/SeedData.cs)
    private const string ValidKey = "funevents-demo-partner-key";
    private const string RevokedKey = "funevents-demo-partner-key-revoked";
    private const string ReadOnlyKey = "funevents-demo-partner-key-readonly";

    private static readonly Guid DemoPartner = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid FunFest = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid Ana = Guid.Parse("b0000000-0000-0000-0000-000000000001");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public ApiKeyTests(FunEventsApiFixture fixture) => _client = fixture.Client;

    // -----------------------------------------------------------------
    // Que sigue siendo publico
    // -----------------------------------------------------------------

    [Fact]
    public async Task El_catalogo_sigue_siendo_publico_sin_credencial()
    {
        var response = await _client.GetAsync("/api/v1/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Los_health_checks_no_exigen_credencial()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------
    // Credencial presente pero invalida
    // -----------------------------------------------------------------

    [Theory]
    [InlineData("clave-que-no-existe")]
    [InlineData(RevokedKey)]
    public async Task Una_api_key_invalida_o_revocada_devuelve_401_en_cualquier_ruta(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events");
        request.Headers.Add("X-Api-Key", key);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCode(response, "INVALID_API_KEY");
    }

    /// <summary>
    /// Revocar un colaborador es un UPDATE sobre <c>IsActive</c>, no borrar la
    /// fila: el historico de reservas tiene que seguir apuntando a alguien.
    /// </summary>
    [Fact]
    public async Task Un_colaborador_dado_de_baja_no_puede_reservar()
    {
        var response = await PostPartnerReservation(RevokedKey);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------
    // Canal Partner
    // -----------------------------------------------------------------

    [Fact]
    public async Task El_canal_Partner_sin_credencial_devuelve_401()
    {
        var response = await PostPartnerReservation(apiKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertErrorCode(response, "API_KEY_REQUIRED");
    }

    /// <summary>
    /// 403 y no 401: la credencial es correcta, lo que falta es el permiso.
    /// Devolver 401 aqui haria que el integrador revisara su clave, que esta
    /// bien, en lugar de su contrato, que es lo que hay que ampliar.
    /// </summary>
    [Fact]
    public async Task Una_api_key_sin_el_scope_de_creacion_devuelve_403()
    {
        var response = await PostPartnerReservation(ReadOnlyKey);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await AssertErrorCode(response, "INSUFFICIENT_SCOPE");
    }

    [Fact]
    public async Task Una_api_key_valida_crea_la_reserva_y_le_asigna_el_colaborador()
    {
        var response = await PostPartnerReservation(ValidKey);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channel").GetString().Should().Be("Partner");
        body.GetProperty("partnerId").GetGuid().Should().Be(DemoPartner);
    }

    /// <summary>
    /// La identidad no puede venir del mismo sitio que los datos: si el cuerpo
    /// pudiera fijar el PartnerId, un colaborador atribuiria sus ventas a otro
    /// cambiando un campo del JSON.
    /// </summary>
    [Fact]
    public async Task El_partnerId_enviado_en_el_cuerpo_se_rechaza_con_400()
    {
        var payload = new
        {
            eventId = FunFest,
            userId = Ana,
            ticketQuantity = 1,
            channel = "Partner",
            partnerId = Guid.NewGuid()
        };

        var response = await Post(payload, ValidKey);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------
    // Aislamiento entre colaboradores
    // -----------------------------------------------------------------

    [Fact]
    public async Task Un_colaborador_no_puede_leer_la_reserva_de_otro()
    {
        var created = await PostPartnerReservation(ValidKey);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var reservationId = body.GetProperty("reservationId").GetGuid();

        var owner = await Get($"/api/v1/reservations/{reservationId}", ValidKey);
        owner.StatusCode.Should().Be(HttpStatusCode.OK);

        var stranger = await Get($"/api/v1/reservations/{reservationId}", ReadOnlyKey);

        // 404 y no 403: un 403 confirmaria que el identificador corresponde a
        // una reserva real, que ya es informacion del negocio de otro.
        stranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -----------------------------------------------------------------
    // Utilidades
    // -----------------------------------------------------------------

    private Task<HttpResponseMessage> PostPartnerReservation(string? apiKey)
        => Post(new
        {
            eventId = FunFest,
            userId = Ana,
            ticketQuantity = 1,
            channel = "Partner"
        }, apiKey);

    private Task<HttpResponseMessage> Post(object payload, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };

        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Get(string url, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (apiKey is not null)
            request.Headers.Add("X-Api-Key", apiKey);

        return _client.SendAsync(request);
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, string expected)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be(expected);
    }
}
