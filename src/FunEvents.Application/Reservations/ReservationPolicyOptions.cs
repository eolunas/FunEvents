namespace FunEvents.Application.Reservations;

/// <summary>
/// Reglas de negocio parametrizables del flujo de reserva.
/// </summary>
/// <remarks>
/// Estaban antes como numeros magicos repartidos por el codigo
/// (<c>AddMinutes(15)</c> en el servicio, <c>LessThanOrEqualTo(10)</c> en el
/// validador). Sacarlas a configuracion permite que negocio las ajuste sin
/// recompilar y, sobre todo, que los tests fuercen ventanas de segundos para
/// probar la caducidad sin esperar 15 minutos.
/// </remarks>
public sealed class ReservationPolicyOptions
{
    public const string SectionName = "ReservationPolicy";

    /// <summary>Maximo de entradas en una sola peticion.</summary>
    public int MaxTicketsPerRequest { get; set; } = 10;

    /// <summary>Maximo acumulado de entradas activas por usuario y evento.</summary>
    public int MaxTicketsPerUserPerEvent { get; set; } = 10;

    /// <summary>Cuanto se retiene el cupo antes de que la reserva caduque.</summary>
    public TimeSpan HoldWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Cuanto se espera a que termine otra peticion que sostiene la misma
    /// Idempotency-Key antes de devolver 409.
    /// </summary>
    public TimeSpan IdempotencyWaitTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Intervalo entre sondeos mientras se espera a esa otra peticion.</summary>
    public TimeSpan IdempotencyPollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Origen (esquema + host, sin barra final) usado para construir la URL
    /// publica de una reserva. Configurable para no fijar en el codigo un
    /// dominio distinto por entorno (local, staging, produccion).
    /// </summary>
    public string ReservationUrlBase { get; set; } = "https://urlbase.co";
}
