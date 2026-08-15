using System.Diagnostics;

namespace FunEvents.ConsoleClient;

/// <summary>
/// Cliente de consola de FunEvents.
/// </summary>
/// <remarks>
/// <para>
/// Dos modos de uso:
/// </para>
/// <list type="bullet">
///   <item><c>dotnet run</c> — menu interactivo.</item>
///   <item><c>dotnet run -- --demo</c> — recorrido completo sin intervencion,
///   pensado para la sustentacion y para ejecutarse en un pipeline.</item>
/// </list>
/// <para>
/// La URL de la API se toma de <c>--url</c>, de la variable de entorno
/// <c>FUNEVENTS_API_URL</c> o, por defecto, de <c>http://localhost:8080</c>.
/// Antes el valor por defecto era el puerto 5000 mientras la API arrancaba en
/// el 5119: recien clonado, el cliente no se conectaba a nada.
/// </para>
/// </remarks>
internal static class Program
{
    private const string DefaultBaseUrl = "http://localhost:8080";

    // Claves de los colaboradores sembrados. Estan aqui, en el cliente, porque
    // es exactamente donde vivirian en la integracion real: la credencial la
    // custodia quien la usa. Las tres existen para poder ensenar los tres
    // resultados distintos que un integrador se puede encontrar.
    private const string DemoPartnerKey = "funevents-demo-partner-key";
    private const string RevokedPartnerKey = "funevents-demo-partner-key-revoked";
    private const string ReadOnlyPartnerKey = "funevents-demo-partner-key-readonly";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var baseUrl = ResolveBaseUrl(args);
        var demoMode = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var client = new FunEventsApiClient(http, ResolveApiKey(args));

        Header(baseUrl);

        if (!await client.IsReachableAsync())
        {
            Error($"No hay respuesta de la API en {baseUrl}.");
            Console.WriteLine();
            Console.WriteLine("  Arranca la solucion completa con:   docker compose up --build");
            Console.WriteLine("  O apunta a otra URL con:            dotnet run -- --url http://localhost:5000");
            return 1;
        }

        if (demoMode)
        {
            await RunFullDemo(client);
            return 0;
        }

        await RunInteractiveMenu(client);
        return 0;
    }

    // ---------------------------------------------------------------------
    // Menu
    // ---------------------------------------------------------------------

    private static async Task RunInteractiveMenu(FunEventsApiClient client)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("  1. Listar eventos");
            Console.WriteLine("  2. Listar usuarios");
            Console.WriteLine("  3. Consultar disponibilidad");
            Console.WriteLine("  4. Reservar entradas");
            Console.WriteLine("  5. Consultar una reserva");
            Console.WriteLine("  6. Simular concurrencia (15 peticiones en paralelo)");
            Console.WriteLine("  7. Simular idempotencia (misma key 3 veces)");
            Console.WriteLine("  8. Canal de colaboradores (API Key y aislamiento)");
            Console.WriteLine("  9. Limite de peticiones (rate limiting)");
            Console.WriteLine(" 10. Demo completa");
            Console.WriteLine("  0. Salir");
            Console.Write("\n  Opcion: ");

            var choice = Console.ReadLine();

            // Entrada redirigida o cerrada (Ctrl+D, tuberia agotada): sin esto
            // el bucle giraria para siempre consumiendo CPU.
            if (choice is null)
            {
                Console.WriteLine("\n(entrada cerrada)");
                return;
            }

            Console.WriteLine();

            try
            {
                switch (choice.Trim())
                {
                    case "1": await ListEvents(client); break;
                    case "2": await ListUsers(client); break;
                    case "3": await CheckAvailability(client); break;
                    case "4": await ReserveTickets(client); break;
                    case "5": await CheckReservation(client); break;
                    case "6": await SimulateConcurrency(client); break;
                    case "7": await SimulateIdempotency(client); break;
                    case "8": await DemoPartnerChannel(client); break;
                    case "9": await DemoRateLimiting(client); break;
                    case "10": await RunFullDemo(client); break;
                    case "0": return;
                    default: Console.WriteLine("  Opcion no valida."); break;
                }
            }
            catch (HttpRequestException ex)
            {
                Error($"No se pudo contactar con la API: {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------------------
    // Operaciones
    // ---------------------------------------------------------------------

    private static async Task<List<EventDto>> ListEvents(FunEventsApiClient client)
    {
        var result = await client.GetEventsAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            Error(result.Describe());
            return new List<EventDto>();
        }

        var items = result.Value.Items;
        if (items.Count == 0)
        {
            Console.WriteLine("  No hay eventos publicados.");
            return items;
        }

        Console.WriteLine("  CODIGO DE EVENTO                      NOMBRE                AFORO  DISPONIBLE");
        Console.WriteLine("  " + new string('-', 78));
        foreach (var e in items)
            Console.WriteLine($"  {e.Id}  {Fit(e.Name, 20)}  {e.Capacity,5}  {e.AvailableCapacity,10}");

        Console.WriteLine($"\n  {result.Value.TotalCount} evento(s), pagina {result.Value.Page} de {Math.Max(result.Value.TotalPages, 1)}");
        return items;
    }

    private static async Task<List<UserDto>> ListUsers(FunEventsApiClient client)
    {
        var result = await client.GetUsersAsync();
        if (!result.IsSuccess || result.Value is null)
        {
            Error(result.Describe());
            return new List<UserDto>();
        }

        Console.WriteLine("  CODIGO DE USUARIO                     NOMBRE                ESTADO");
        Console.WriteLine("  " + new string('-', 70));
        foreach (var u in result.Value)
            Console.WriteLine($"  {u.Id}  {Fit(u.FullName, 20)}  {(u.IsActive ? "activo" : "INACTIVO")}");

        return result.Value;
    }

    private static async Task CheckAvailability(FunEventsApiClient client)
    {
        var eventId = await AskEventId(client);
        if (eventId is null) return;

        var result = await client.GetAvailabilityAsync(eventId.Value);
        if (!result.IsSuccess || result.Value is null)
        {
            Error(result.Describe());
            return;
        }

        var a = result.Value;
        Console.WriteLine($"  Evento:      {a.EventName}");
        Console.WriteLine($"  Aforo:       {a.TotalCapacity}");
        Console.WriteLine($"  Reservadas:  {a.ReservedCount}");
        Console.WriteLine($"  Disponibles: {a.AvailableCount}");
        Console.WriteLine($"  A la venta:  {(a.IsOpenForSale ? "si" : "no")}");
        Console.WriteLine($"  Medido a:    {a.AsOf:HH:mm:ss} UTC");
    }

    private static async Task ReserveTickets(FunEventsApiClient client)
    {
        var eventId = await AskEventId(client);
        if (eventId is null) return;

        var userId = await AskUserId(client);
        if (userId is null) return;

        Console.Write("  Numero de entradas: ");
        if (!int.TryParse(Console.ReadLine(), out var quantity) || quantity < 1)
        {
            Error("Cantidad no valida.");
            return;
        }

        var key = Guid.NewGuid().ToString();
        Console.WriteLine($"  Idempotency-Key: {key}");

        var result = await client.CreateReservationAsync(eventId.Value, userId.Value, quantity, key);
        PrintReservation(result);
    }

    private static async Task CheckReservation(FunEventsApiClient client)
    {
        Console.Write("  Codigo de reserva: ");
        if (!Guid.TryParse(Console.ReadLine(), out var id))
        {
            Error("Codigo no valido.");
            return;
        }

        PrintReservation(await client.GetReservationAsync(id));
    }

    /// <summary>
    /// Lanza mas peticiones simultaneas que plazas quedan y comprueba que el
    /// numero de reservas confirmadas coincide EXACTAMENTE con el aforo libre.
    /// Es la demostracion de que el control de concurrencia funciona.
    /// </summary>
    private static async Task SimulateConcurrency(FunEventsApiClient client)
    {
        const int attempts = 15;

        var events = await GetEventsQuietly(client);
        var target = events.FirstOrDefault(e => e.AvailableCapacity is > 0 and <= 10)
                     ?? events.MinBy(e => e.AvailableCapacity);

        if (target is null)
        {
            Error("No hay ningun evento con aforo disponible para la prueba.");
            return;
        }

        var before = target.AvailableCapacity;

        Console.WriteLine($"  Evento:              {target.Name}");
        Console.WriteLine($"  Aforo disponible:    {before}");
        Console.WriteLine($"  Peticiones a lanzar: {attempts} (1 entrada cada una, en paralelo)\n");

        var stopwatch = Stopwatch.StartNew();

        // Un usuario distinto por peticion: si se usara el mismo, el limite de
        // entradas por usuario rechazaria la mayoria y la prueba mediria esa
        // regla en lugar del control de aforo.
        var users = await GetUsersQuietly(client);
        var activeUsers = users.Where(u => u.IsActive).ToList();
        if (activeUsers.Count == 0)
        {
            Error("No hay usuarios activos.");
            return;
        }

        var tasks = Enumerable.Range(0, attempts).Select(async i =>
        {
            var user = activeUsers[i % activeUsers.Count];
            var result = await client.CreateReservationAsync(
                target.Id, user.Id, 1, $"concurrency-{Guid.NewGuid()}");
            return (Index: i, Result: result);
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        Console.WriteLine("   #   RESULTADO   DETALLE");
        Console.WriteLine("  " + new string('-', 74));
        foreach (var (index, result) in results.OrderBy(r => r.Index))
        {
            var label = result.IsSuccess ? "OK      " : "RECHAZO ";
            var detail = result.IsSuccess
                ? result.Value!.ReservationId.ToString()
                : result.Describe();
            Console.WriteLine($"  {index,2}   {label}    {detail}");
        }

        var accepted = results.Count(r => r.Result.IsSuccess);
        var rejected = attempts - accepted;

        Console.WriteLine();
        Console.WriteLine($"  Aceptadas: {accepted}   Rechazadas: {rejected}   Tiempo: {stopwatch.ElapsedMilliseconds} ms");

        var expected = Math.Min(before, attempts);
        if (accepted == expected)
            Ok($"Correcto: se aceptaron exactamente las {expected} plazas que quedaban. Sin sobreventa.");
        else
            Error($"ATENCION: se esperaban {expected} aceptadas y hubo {accepted}.");

        var after = await client.GetAvailabilityAsync(target.Id);
        if (after.IsSuccess && after.Value is not null)
            Console.WriteLine($"  Aforo disponible tras la prueba: {after.Value.AvailableCount}");
    }

    /// <summary>
    /// Repite la MISMA peticion con la MISMA Idempotency-Key y comprueba que
    /// solo se crea una reserva.
    /// </summary>
    private static async Task SimulateIdempotency(FunEventsApiClient client)
    {
        var events = await GetEventsQuietly(client);
        var target = events.FirstOrDefault(e => e.AvailableCapacity > 0);
        var users = await GetUsersQuietly(client);
        var user = users.FirstOrDefault(u => u.IsActive);

        if (target is null || user is null)
        {
            Error("Hacen falta un evento con aforo y un usuario activo.");
            return;
        }

        var key = $"idempotency-{Guid.NewGuid()}";
        Console.WriteLine($"  Evento:          {target.Name}");
        Console.WriteLine($"  Usuario:         {user.FullName}");
        Console.WriteLine($"  Idempotency-Key: {key}");
        Console.WriteLine("\n  Enviando la misma peticion 3 veces...\n");

        var ids = new List<Guid>();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var result = await client.CreateReservationAsync(target.Id, user.Id, 1, key);

            if (result.IsSuccess && result.Value is not null)
            {
                ids.Add(result.Value.ReservationId);
                Console.WriteLine($"  Intento {attempt}: HTTP {result.StatusCode}  " +
                                  $"reserva {result.Value.ReservationId}  " +
                                  $"previouslyCreated={result.Value.PreviouslyCreated}");
            }
            else
            {
                Console.WriteLine($"  Intento {attempt}: {result.Describe()}");
            }

            if (attempt < 3) await Task.Delay(300);
        }

        Console.WriteLine();
        if (ids.Count == 3 && ids.Distinct().Count() == 1)
            Ok($"Correcto: los 3 intentos devolvieron la misma reserva ({ids[0]}). Una sola compra.");
        else
            Error($"ATENCION: se obtuvieron {ids.Distinct().Count()} reserva(s) distintas.");

        // Misma key, cuerpo distinto: la API debe rechazarlo en lugar de
        // devolver silenciosamente la reserva anterior.
        Console.WriteLine("\n  Reutilizando la MISMA key con una cantidad distinta...");
        var reused = await client.CreateReservationAsync(target.Id, user.Id, 3, key);

        if (reused.StatusCode == 422 && reused.Problem?.ErrorCode == "IDEMPOTENCY_KEY_REUSED")
            Ok("Correcto: rechazado con IDEMPOTENCY_KEY_REUSED.");
        else
            Error($"Se esperaba 422 IDEMPOTENCY_KEY_REUSED y se obtuvo: {reused.Describe()}");
    }

    private static async Task RunFullDemo(FunEventsApiClient client)
    {
        Section("1. Catalogo de eventos");
        await ListEvents(client);

        Section("2. Usuarios conocidos");
        await ListUsers(client);

        Section("3. Reserva de punta a punta");
        var events = await GetEventsQuietly(client);
        var users = await GetUsersQuietly(client);
        var target = events.FirstOrDefault(e => e.AvailableCapacity > 0);
        var user = users.FirstOrDefault(u => u.IsActive);

        if (target is not null && user is not null)
        {
            Console.WriteLine($"  Reservando 2 entradas de '{target.Name}' para {user.FullName}...\n");
            var created = await client.CreateReservationAsync(
                target.Id, user.Id, 2, $"demo-{Guid.NewGuid()}");
            PrintReservation(created);

            if (created.IsSuccess && created.Value is not null)
            {
                Console.WriteLine("\n  Releyendo la reserva desde la API...");
                PrintReservation(await client.GetReservationAsync(created.Value.ReservationId));
            }
        }

        Section("4. Rechazo por usuario inactivo");
        var inactive = users.FirstOrDefault(u => !u.IsActive);
        if (inactive is not null && target is not null)
        {
            var rejected = await client.CreateReservationAsync(
                target.Id, inactive.Id, 1, $"demo-inactive-{Guid.NewGuid()}");
            Console.WriteLine($"  {rejected.Describe()}");
        }

        Section("5. Idempotencia");
        await SimulateIdempotency(client);

        Section("6. Canal de colaboradores: API Key, scopes y aislamiento");
        await DemoPartnerChannel(client);

        Section("7. Limite de peticiones por colaborador");
        await DemoRateLimiting(client);

        Section("8. Concurrencia");
        await SimulateConcurrency(client);

        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("  DEMO COMPLETA");
        Console.WriteLine(new string('=', 80));
    }

    // ---------------------------------------------------------------------
    // Utilidades
    // ---------------------------------------------------------------------

    private static async Task<List<EventDto>> GetEventsQuietly(FunEventsApiClient client)
        => (await client.GetEventsAsync()).Value?.Items ?? new List<EventDto>();

    private static async Task<List<UserDto>> GetUsersQuietly(FunEventsApiClient client)
        => (await client.GetUsersAsync()).Value ?? new List<UserDto>();

    private static async Task<Guid?> AskEventId(FunEventsApiClient client)
    {
        var events = await ListEvents(client);
        if (events.Count == 0) return null;

        Console.Write("\n  Codigo de evento (Enter = el primero): ");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input)) return events[0].Id;
        if (Guid.TryParse(input.Trim(), out var id)) return id;

        Error("Codigo de evento no valido.");
        return null;
    }

    private static async Task<Guid?> AskUserId(FunEventsApiClient client)
    {
        var users = await ListUsers(client);
        if (users.Count == 0) return null;

        Console.Write("\n  Codigo de usuario (Enter = el primero activo): ");
        var input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            return users.FirstOrDefault(u => u.IsActive)?.Id ?? users[0].Id;

        if (Guid.TryParse(input.Trim(), out var id)) return id;

        Error("Codigo de usuario no valido.");
        return null;
    }

    private static void PrintReservation(ApiResult<ReservationResponse> result)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            Error(result.Describe());
            return;
        }

        var r = result.Value;
        Console.WriteLine($"  HTTP {result.StatusCode}" +
                          (r.PreviouslyCreated ? "  (reproduccion idempotente)" : "  (reserva nueva)"));
        Console.WriteLine($"  Reserva:  {r.ReservationId}");
        Console.WriteLine($"  Evento:   {r.EventName}");
        Console.WriteLine($"  Usuario:  {r.UserName}");
        Console.WriteLine($"  Entradas: {r.TicketQuantity}");
        Console.WriteLine($"  Estado:   {r.State}");
        Console.WriteLine($"  Canal:    {r.Channel}");
        if (r.PartnerId is { } partnerId)
            Console.WriteLine($"  Partner:  {partnerId}  (derivado de la API Key, no del cuerpo)");
        Console.WriteLine($"  Caduca:   {r.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
    }

    // ---------------------------------------------------------------------
    // Canal de colaboradores
    // ---------------------------------------------------------------------

    /// <summary>
    /// Recorre los cuatro resultados que puede obtener un colaborador al
    /// reservar: sin clave, con clave revocada, con clave sin permiso y con
    /// clave valida. Y despues comprueba que un colaborador no puede leer la
    /// reserva de otro.
    /// </summary>
    private static async Task DemoPartnerChannel(FunEventsApiClient client)
    {
        var events = await GetEventsQuietly(client);
        var users = await GetUsersQuietly(client);

        var target = events.FirstOrDefault(e => e.AvailableCapacity > 0);
        var user = users.FirstOrDefault(u => u.IsActive);

        if (target is null || user is null)
        {
            Error("No hay evento con aforo o usuario activo para la demostracion.");
            return;
        }

        async Task<ApiResult<ReservationResponse>> ReserveAs(string? key) =>
            await client.WithApiKey(key).CreateReservationAsync(
                target.Id, user.Id, 1, $"partner-{Guid.NewGuid()}", channel: "Partner");

        Console.WriteLine("  Canal Partner SIN cabecera X-Api-Key");
        Console.WriteLine($"    -> {(await ReserveAs(null)).Describe()}");
        Console.WriteLine("       Esperado: 401 API_KEY_REQUIRED\n");

        Console.WriteLine("  Canal Partner con una clave inexistente");
        Console.WriteLine($"    -> {(await ReserveAs("clave-que-no-existe")).Describe()}");
        Console.WriteLine("       Esperado: 401 INVALID_API_KEY\n");

        Console.WriteLine("  Canal Partner con la clave de un colaborador dado de baja");
        Console.WriteLine($"    -> {(await ReserveAs(RevokedPartnerKey)).Describe()}");
        Console.WriteLine("       Esperado: 401. Revocar es un UPDATE, no borrar la fila.\n");

        Console.WriteLine("  Canal Partner con una clave valida pero sin el permiso reservations:create");
        Console.WriteLine($"    -> {(await ReserveAs(ReadOnlyPartnerKey)).Describe()}");
        Console.WriteLine("       Esperado: 403 INSUFFICIENT_SCOPE. 403 y no 401: la credencial es buena.\n");

        Console.WriteLine("  Canal Partner con la clave del colaborador autorizado");
        var created = await ReserveAs(DemoPartnerKey);
        PrintReservation(created);

        if (!created.IsSuccess || created.Value is null) return;

        Console.WriteLine("\n  Aislamiento entre colaboradores:");

        var own = await client.WithApiKey(DemoPartnerKey)
            .GetReservationAsync(created.Value.ReservationId);
        Console.WriteLine($"    El colaborador propietario la lee   -> {own.Describe()}");

        var foreign = await client.WithApiKey(ReadOnlyPartnerKey)
            .GetReservationAsync(created.Value.ReservationId);
        Console.WriteLine($"    Otro colaborador la lee             -> {foreign.Describe()}");
        Console.WriteLine("       Esperado: 404 y no 403. Un 403 confirmaria que la reserva existe.");
    }

    /// <summary>
    /// Demuestra el limitador contra el colaborador de solo lectura, cuyo cupo
    /// contratado son 60 peticiones por minuto.
    /// </summary>
    private static async Task DemoRateLimiting(FunEventsApiClient client)
    {
        const int contractedLimit = 60;

        Console.WriteLine($"  Colaborador 'Portal Solo Consulta' — cupo contratado: {contractedLimit} pet/min.");
        Console.WriteLine("  Lanzando peticiones al catalogo hasta recibir un 429...\n");

        var (requests, status, retryAfter) = await client
            .WithApiKey(ReadOnlyPartnerKey)
            .ProbeRateLimitAsync(contractedLimit + 10);

        if (status == 429)
        {
            Ok($"429 en la peticion numero {requests}. Retry-After: {retryAfter ?? "n/d"} s.");
            Console.WriteLine("  El cupo es por colaborador: el resto de socios no se ve afectado.");
        }
        else
        {
            Console.WriteLine($"  No se alcanzo el limite en {requests} peticiones.");
            Console.WriteLine("  (El limitador puede estar desactivado en la configuracion.)");
        }
    }

    // ---------------------------------------------------------------------
    // Configuracion
    // ---------------------------------------------------------------------

    private static string? ResolveApiKey(string[] args)
    {
        var flagIndex = Array.FindIndex(args, a => a.Equals("--api-key", StringComparison.OrdinalIgnoreCase));
        if (flagIndex >= 0 && flagIndex + 1 < args.Length)
            return args[flagIndex + 1];

        return Environment.GetEnvironmentVariable("FUNEVENTS_API_KEY");
    }

    private static string ResolveBaseUrl(string[] args)
    {
        var flagIndex = Array.FindIndex(args, a => a.Equals("--url", StringComparison.OrdinalIgnoreCase));
        if (flagIndex >= 0 && flagIndex + 1 < args.Length)
            return args[flagIndex + 1];

        // Compatibilidad: primer argumento posicional que parezca una URL.
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out _))
            return args[0];

        return Environment.GetEnvironmentVariable("FUNEVENTS_API_URL") ?? DefaultBaseUrl;
    }

    private static string Fit(string value, int width)
        => value.Length <= width ? value.PadRight(width) : value[..(width - 3)] + "...";

    private static void Header(string baseUrl)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("  FunEvents - Cliente de consola");
        Console.WriteLine($"  API: {baseUrl}");
        Console.WriteLine(new string('=', 80));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 80));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('-', 80));
    }

    private static void Ok(string message) => WriteColored("  " + message, ConsoleColor.Green);

    private static void Error(string message) => WriteColored("  " + message, ConsoleColor.Red);

    private static void WriteColored(string message, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }
}
