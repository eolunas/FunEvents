using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace FunEvents.IntegrationTests.Reservations;

/// <summary>
/// Flujo de reserva de punta a punta: HTTP -> controlador -> caso de uso ->
/// PostgreSQL real.
/// </summary>
[Collection(FunEventsApiCollection.Name)]
public class CreateReservationTests
{
    // Codigos sembrados (src/FunEvents.Infrastructure/Data/SeedData.cs)
    private static readonly Guid FunFest = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid ComedyNight = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid DraftEvent = Guid.Parse("a0000000-0000-0000-0000-000000000004");
    private static readonly Guid Ana = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid Carlos = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    private static readonly Guid InactiveUser = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;

    public CreateReservationTests(FunEventsApiFixture fixture) => _client = fixture.Client;

    // -----------------------------------------------------------------
    // Camino feliz
    // -----------------------------------------------------------------

    [Fact]
    public async Task Con_aforo_disponible_devuelve_201_y_los_datos_de_la_reserva()
    {
        var response = await PostReservation(FunFest, Ana, 2, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reservationId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("userId").GetGuid().Should().Be(Ana);
        body.GetProperty("ticketQuantity").GetInt32().Should().Be(2);
        body.GetProperty("state").GetString().Should().Be("Reserved");
        body.GetProperty("channel").GetString().Should().Be("Online");
        body.GetProperty("previouslyCreated").GetBoolean().Should().BeFalse();

        // La cabecera Location debe apuntar al recurso creado y ese GET debe funcionar.
        response.Headers.Location.Should().NotBeNull();
        var followUp = await _client.GetAsync(response.Headers.Location);
        followUp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reservar_descuenta_el_aforo_disponible()
    {
        var before = await GetAvailableCount(FunFest);

        var response = await PostReservation(FunFest, Carlos, 3, NewKey());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await GetAvailableCount(FunFest);
        after.Should().Be(before - 3);
    }

    // -----------------------------------------------------------------
    // Idempotencia
    // -----------------------------------------------------------------

    [Fact]
    public async Task Repetir_la_peticion_con_la_misma_key_devuelve_200_y_la_misma_reserva()
    {
        var key = NewKey();

        var first = await PostReservation(FunFest, Ana, 1, key);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostReservation(FunFest, Ana, 1, key);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("reservationId").GetGuid();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        secondBody.GetProperty("reservationId").GetGuid().Should().Be(firstId);
        secondBody.GetProperty("previouslyCreated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Repetir_la_peticion_con_la_misma_key_no_consume_aforo_dos_veces()
    {
        var key = NewKey();
        var before = await GetAvailableCount(FunFest);

        await PostReservation(FunFest, Ana, 2, key);
        await PostReservation(FunFest, Ana, 2, key);
        await PostReservation(FunFest, Ana, 2, key);

        var after = await GetAvailableCount(FunFest);
        after.Should().Be(before - 2, "las tres peticiones son el mismo intento");
    }

    [Fact]
    public async Task Reutilizar_una_key_con_otro_cuerpo_devuelve_422()
    {
        var key = NewKey();

        (await PostReservation(FunFest, Ana, 1, key)).StatusCode.Should().Be(HttpStatusCode.Created);

        var reused = await PostReservation(FunFest, Ana, 5, key);

        reused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCode(reused, "IDEMPOTENCY_KEY_REUSED");
    }

    [Fact]
    public async Task Tres_peticiones_simultaneas_con_la_misma_key_crean_una_sola_reserva()
    {
        var key = NewKey();

        var responses = await Task.WhenAll(
            PostReservation(FunFest, Carlos, 1, key),
            PostReservation(FunFest, Carlos, 1, key),
            PostReservation(FunFest, Carlos, 1, key));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created)
            .Should().BeLessThanOrEqualTo(1, "solo una peticion puede crear la reserva");

        var reservationIds = new List<Guid>();
        foreach (var response in responses.Where(r => r.IsSuccessStatusCode))
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            reservationIds.Add(body.GetProperty("reservationId").GetGuid());
        }

        reservationIds.Distinct().Count().Should().BeLessThanOrEqualTo(1,
            "todas las respuestas exitosas deben referirse a la misma reserva");
    }

    // -----------------------------------------------------------------
    // Concurrencia: la prueba que de verdad importa
    // -----------------------------------------------------------------

    [Fact]
    public async Task Mas_peticiones_que_plazas_nunca_produce_sobreventa()
    {
        var available = await GetAvailableCount(ComedyNight);
        available.Should().BeGreaterThan(0, "el evento sembrado debe tener aforo libre");

        var attempts = available + 5;

        var responses = await Task.WhenAll(
            Enumerable.Range(0, attempts)
                .Select(i => PostReservation(ComedyNight, i % 2 == 0 ? Ana : Carlos, 1, NewKey())));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        created.Should().Be(available,
            "deben entrar exactamente las plazas que quedaban, ni una mas");
        conflicts.Should().Be(attempts - available,
            "el resto debe rechazarse con 409, no fallar de otra forma");

        (await GetAvailableCount(ComedyNight)).Should().Be(0);
    }

    // -----------------------------------------------------------------
    // Reglas de negocio y validacion
    // -----------------------------------------------------------------

    [Fact]
    public async Task Un_evento_sin_publicar_devuelve_422()
    {
        var response = await PostReservation(DraftEvent, Ana, 1, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCode(response, "EVENT_NOT_PUBLISHED");
    }

    [Fact]
    public async Task Un_usuario_inactivo_devuelve_422()
    {
        var response = await PostReservation(FunFest, InactiveUser, 1, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await AssertErrorCode(response, "USER_INACTIVE");
    }

    [Fact]
    public async Task Un_evento_inexistente_devuelve_404()
    {
        var response = await PostReservation(Guid.NewGuid(), Ana, 1, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCode(response, "EVENT_NOT_FOUND");
    }

    [Fact]
    public async Task Un_usuario_inexistente_devuelve_404()
    {
        var response = await PostReservation(FunFest, Guid.NewGuid(), 1, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await AssertErrorCode(response, "USER_NOT_FOUND");
    }

    [Fact]
    public async Task Sin_el_header_Idempotency_Key_devuelve_400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = JsonContent(FunFest, Ana, 1)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public async Task Una_cantidad_fuera_de_rango_devuelve_400(int quantity)
    {
        // Regresion: el validador FluentValidation existia pero nadie lo
        // ejecutaba, asi que estas peticiones no se rechazaban aqui.
        var response = await PostReservation(FunFest, Ana, quantity, NewKey());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sin_userId_devuelve_400()
    {
        // Regresion del hueco frente al enunciado: la API aceptaba reservas sin
        // usuario, cuando el enunciado pide reservar a partir de un codigo de
        // evento Y de usuario.
        var payload = new { eventId = FunFest, ticketQuantity = 1 };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", NewKey());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// El canal Partner exige credencial. Antes de implementar la autenticacion,
    /// esta peticion se aceptaba con solo declarar un PartnerId en el cuerpo.
    /// El resto de casos del canal esta en <c>Security/ApiKeyTests</c>.
    /// </summary>
    [Fact]
    public async Task El_canal_Partner_sin_api_key_devuelve_401()
    {
        var payload = new
        {
            eventId = FunFest,
            userId = Ana,
            ticketQuantity = 1,
            channel = "Partner"
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", NewKey());

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------
    // Utilidades
    // -----------------------------------------------------------------

    private static string NewKey() => Guid.NewGuid().ToString();

    private Task<HttpResponseMessage> PostReservation(Guid eventId, Guid userId, int quantity, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = JsonContent(eventId, userId, quantity)
        };
        request.Headers.Add("Idempotency-Key", key);

        return _client.SendAsync(request);
    }

    private static StringContent JsonContent(Guid eventId, Guid userId, int quantity)
    {
        var payload = new { eventId, userId, ticketQuantity = quantity, channel = "Online" };
        return new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
    }

    private async Task<int> GetAvailableCount(Guid eventId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/events/{eventId}/availability");

        // El endpoint de disponibilidad se cachea 5 segundos, que es lo correcto
        // en produccion pero haria estos tests intermitentes: leeriamos el aforo
        // de antes de la reserva. Cache-Control: no-cache le dice al middleware
        // de response caching que ignore la copia guardada.
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true
        };

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("availableCount").GetInt32();
    }

    private static async Task AssertErrorCode(HttpResponseMessage response, string expectedCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be(expectedCode);
    }
}
