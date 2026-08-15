using FunEvents.Domain.Events;
using FunEvents.Domain.Partners;
using FunEvents.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data;

/// <summary>
/// Datos de arranque para la demo: los "codigos de evento y de usuario ya
/// conocidos" que pide el enunciado.
/// </summary>
/// <remarks>
/// Todos los valores son deterministas (GUIDs y fechas fijas). Si alguno
/// dependiera de <c>DateTimeOffset.UtcNow</c>, cada ejecucion de
/// <c>dotnet ef migrations add</c> generaria una migracion espuria.
/// </remarks>
public static class SeedData
{
    // --- Eventos ---
    public static readonly Guid FunFestId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid TechConfId = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    public static readonly Guid ComedyNightId = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    public static readonly Guid SoldOutId = Guid.Parse("a0000000-0000-0000-0000-000000000004");

    // --- Colaboradores ---
    public static readonly Guid DemoPartnerId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid RevokedPartnerId = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    public static readonly Guid ReadOnlyPartnerId = Guid.Parse("c0000000-0000-0000-0000-000000000003");

    /// <summary>
    /// Claves en claro de la demostracion.
    /// </summary>
    /// <remarks>
    /// <b>Esto es aceptable exactamente aqui y en ningun otro sitio.</b> Son
    /// credenciales de datos de ejemplo, fijas a proposito para que quien revise
    /// el prototipo pueda ejercitar el canal de colaboradores sin darse de alta.
    /// En la tabla se persiste unicamente el SHA-256; la clave en claro no se
    /// almacena ni se registra en ningun log. En un despliegue real, el alta de
    /// un colaborador usa <see cref="ApiKeyHasher.Generate"/>, que devuelve la
    /// clave una sola vez y guarda solo su hash.
    /// </remarks>
    public const string DemoPartnerApiKey = "funevents-demo-partner-key";
    public const string RevokedPartnerApiKey = "funevents-demo-partner-key-revoked";
    public const string ReadOnlyPartnerApiKey = "funevents-demo-partner-key-readonly";

    // --- Usuarios ---
    public static readonly Guid AnaId = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid CarlosId = Guid.Parse("b0000000-0000-0000-0000-000000000002");
    public static readonly Guid InactiveUserId = Guid.Parse("b0000000-0000-0000-0000-000000000003");

    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>().HasData(
            PublishedEvent(FunFestId, "FunFest 2026",
                "El festival de musica mas grande del ano", "Estadio Nacional",
                new DateTimeOffset(2026, 12, 15, 18, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 12, 16, 2, 0, 0, TimeSpan.Zero),
                capacity: 100),

            PublishedEvent(TechConfId, "TechConf 2026",
                "Conferencia de tecnologia y desarrollo de software", "Centro de Convenciones",
                new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 3, 18, 0, 0, TimeSpan.Zero),
                capacity: 25),

            // Aforo pequeno a proposito: es el evento sobre el que se demuestra
            // la concurrencia (15 peticiones simultaneas, 10 plazas).
            PublishedEvent(ComedyNightId, "Comedy Night",
                "Noche de stand-up comedy con los mejores comediantes", "Teatro Municipal",
                new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 20, 23, 0, 0, TimeSpan.Zero),
                capacity: 10),

            // Evento en borrador: sirve para probar el 422 (evento no publicado).
            DraftEvent(SoldOutId, "Ensayo General (no publicado)",
                "Evento en estado Draft para probar el rechazo por estado", "Sala de Ensayo",
                new DateTimeOffset(2026, 11, 5, 19, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 5, 21, 0, 0, TimeSpan.Zero),
                capacity: 50)
        );

        modelBuilder.Entity<Partner>().HasData(
            // Colaborador de demostracion: permisos completos.
            SeedPartner(DemoPartnerId, "Ticketera Aliada S.A.", DemoPartnerApiKey,
                Partner.DefaultScopes, rateLimit: 500, isActive: true),

            // Colaborador dado de baja: sirve para comprobar que una clave
            // revocada devuelve 401 sin necesidad de borrar la fila.
            SeedPartner(RevokedPartnerId, "Colaborador Dado de Baja", RevokedPartnerApiKey,
                Partner.DefaultScopes, rateLimit: 500, isActive: false),

            // Colaborador con permisos de solo lectura: sirve para comprobar el
            // 403 por falta de scope, que es distinto del 401 por credencial.
            SeedPartner(ReadOnlyPartnerId, "Portal Solo Consulta", ReadOnlyPartnerApiKey,
                new[] { "events:read", "reservations:read" }, rateLimit: 60, isActive: true)
        );

        modelBuilder.Entity<User>().HasData(
            SeedUser(AnaId, "Ana Martinez", "ana.martinez@example.com", isActive: true),
            SeedUser(CarlosId, "Carlos Rojas", "carlos.rojas@example.com", isActive: true),
            // Usuario desactivado: sirve para probar el rechazo por usuario inactivo.
            SeedUser(InactiveUserId, "Usuario Inactivo", "inactivo@example.com", isActive: false)
        );
    }

    private static Event DraftEvent(Guid id, string name, string description, string venue,
        DateTimeOffset start, DateTimeOffset end, int capacity)
    {
        var @event = new Event(name, description, venue, start, end, capacity)
        {
            Id = id,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt
        };
        return @event;
    }

    private static Event PublishedEvent(Guid id, string name, string description, string venue,
        DateTimeOffset start, DateTimeOffset end, int capacity)
    {
        var @event = new Event(name, description, venue, start, end, capacity) { Id = id };
        @event.Publish();

        // Se fijan DESPUES de Publish(): el metodo de dominio actualiza UpdatedAt
        // con la hora actual y eso haria el seed no determinista.
        @event.CreatedAt = SeededAt;
        @event.UpdatedAt = SeededAt;
        return @event;
    }

    private static Partner SeedPartner(Guid id, string name, string apiKey,
        IEnumerable<string> scopes, int rateLimit, bool isActive)
    {
        var partner = new Partner(name, ApiKeyHasher.Hash(apiKey), scopes, rateLimit) { Id = id };
        if (!isActive) partner.Deactivate();

        partner.CreatedAt = SeededAt;
        partner.UpdatedAt = SeededAt;
        return partner;
    }

    private static User SeedUser(Guid id, string fullName, string email, bool isActive)
    {
        var user = new User(fullName, email) { Id = id };
        if (!isActive) user.Deactivate();

        user.CreatedAt = SeededAt;
        user.UpdatedAt = SeededAt;
        return user;
    }
}
