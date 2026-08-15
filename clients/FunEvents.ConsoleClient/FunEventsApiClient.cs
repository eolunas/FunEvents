using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FunEvents.ConsoleClient;

/// <summary>
/// Cliente HTTP de la API de FunEvents.
/// </summary>
/// <remarks>
/// <para>
/// Toda llamada devuelve <see cref="ApiResult{T}"/>: nunca lanza por un status
/// de error. La version anterior usaba <c>GetFromJsonAsync</c>, que lanza
/// <c>HttpRequestException</c> ante cualquier respuesta no exitosa, y devolvia
/// <c>null</c> en los fallos del POST. El resultado era que un 409 por falta de
/// aforo -que es una respuesta perfectamente normal de este sistema- llegaba a
/// la consola como "Error" generico, sin el codigo ni el detalle que la API si
/// habia enviado.
/// </para>
/// <para>
/// Es exactamente el tipo de detalle que decide si un colaborador puede
/// integrarse solo o tiene que abrir un ticket.
/// </para>
/// </remarks>
public class FunEventsApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public FunEventsApiClient(HttpClient http, string? apiKey = null)
    {
        _http = http;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Devuelve un cliente equivalente que presenta otra credencial.
    /// </summary>
    /// <remarks>
    /// Comparte el mismo <see cref="HttpClient"/> a proposito: la credencial se
    /// adjunta a cada peticion, no al cliente. Ponerla como cabecera por defecto
    /// del <c>HttpClient</c> obligaria a un socket pool por colaborador y haria
    /// imposible que una misma aplicacion hablara en nombre de varios.
    /// </remarks>
    public FunEventsApiClient WithApiKey(string? apiKey) => new(_http, apiKey);

    public Task<ApiResult<PagedResponse<EventDto>>> GetEventsAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default)
        => SendAsync<PagedResponse<EventDto>>(
            new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events?page={page}&pageSize={pageSize}"), ct);

    public Task<ApiResult<List<UserDto>>> GetUsersAsync(CancellationToken ct = default)
        => SendAsync<List<UserDto>>(new HttpRequestMessage(HttpMethod.Get, "/api/v1/users"), ct);

    public Task<ApiResult<AvailabilityResponse>> GetAvailabilityAsync(
        Guid eventId, CancellationToken ct = default)
        => SendAsync<AvailabilityResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"/api/v1/events/{eventId}/availability"), ct);

    public Task<ApiResult<ReservationResponse>> GetReservationAsync(
        Guid reservationId, CancellationToken ct = default)
        => SendAsync<ReservationResponse>(
            new HttpRequestMessage(HttpMethod.Get, $"/api/v1/reservations/{reservationId}"), ct);

    /// <summary>
    /// Numero de peticiones GET al catalogo hasta recibir un 429, con un tope.
    /// Se usa para demostrar el limitador sin depender de un reloj.
    /// </summary>
    public async Task<(int Requests, int StatusCode, string? RetryAfter)> ProbeRateLimitAsync(
        int maxRequests, CancellationToken ct = default)
    {
        for (var i = 1; i <= maxRequests; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?page=1&pageSize=1");

            if (!string.IsNullOrWhiteSpace(_apiKey))
                request.Headers.Add("X-Api-Key", _apiKey);

            using var response = await _http.SendAsync(request, ct);

            if ((int)response.StatusCode == 429)
                return (i, 429, response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0"));
        }

        return (maxRequests, 200, null);
    }

    public Task<ApiResult<ReservationResponse>> CreateReservationAsync(
        Guid eventId, Guid userId, int quantity, string idempotencyKey,
        string channel = "Online", CancellationToken ct = default)
    {
        var payload = new
        {
            eventId,
            userId,
            ticketQuantity = quantity,
            channel
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reservations")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json")
        };

        // La Idempotency-Key la genera SIEMPRE el cliente, no el servidor: es lo
        // que permite que un reintento tras un timeout de red se reconozca como
        // el mismo intento y no como una compra nueva.
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return SendAsync<ReservationResponse>(request, ct);
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);

        using var response = await _http.SendAsync(request, ct);
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            // 204 y cuerpos vacios no se pueden deserializar.
            if (response.Content.Headers.ContentLength is 0 or null)
                return new ApiResult<T> { StatusCode = status };

            var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return new ApiResult<T> { StatusCode = status, Value = value };
        }

        ProblemDetailsDto? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(Json, ct);
        }
        catch (JsonException)
        {
            // El cuerpo no era problem+json (por ejemplo, un 502 de un proxy).
            // Se ignora: el status por si solo ya es informacion util.
        }

        return new ApiResult<T> { StatusCode = status, Problem = problem };
    }
}
