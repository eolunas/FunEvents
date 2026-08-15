namespace FunEvents.Api.Security;

/// <summary>Configuracion de la seccion <c>Security</c> de appsettings.</summary>
public class SecurityOptions
{
    public const string SectionName = "Security";

    public ApiKeyOptions ApiKey { get; set; } = new();

    public RateLimitingOptions RateLimiting { get; set; } = new();
}

public class ApiKeyOptions
{
    /// <summary>Cabecera en la que viaja la clave del colaborador.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Cuanto se cachea en memoria la resolucion clave -> colaborador.
    /// </summary>
    /// <remarks>
    /// Es el parametro que fija el compromiso entre carga y revocacion. A 0
    /// segundos, cada peticion del colaborador es una consulta a la base de
    /// datos. A 5 minutos, un colaborador dado de baja seguiria operando 5
    /// minutos. 30 segundos mantiene la revocacion dentro de lo que un operador
    /// percibe como "inmediato" y elimina el 99 % de las consultas en trafico
    /// sostenido.
    /// </remarks>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}

public class RateLimitingOptions
{
    /// <summary>
    /// Permite apagar el limitador. Los tests de integracion lo desactivan para
    /// que la prueba de concurrencia (15 peticiones simultaneas) mida el control
    /// de aforo y no choque contra el limitador.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Ventana de conteo.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Peticiones por ventana para trafico sin credencial. Es deliberadamente
    /// generoso: detras de un NAT corporativo, muchos usuarios legitimos
    /// comparten IP.
    /// </summary>
    public int AnonymousPermitLimit { get; set; } = 300;

    /// <summary>
    /// Peticiones encoladas antes de rechazar. Cero a proposito: encolar bajo
    /// carga convierte un 429 inmediato (que el cliente puede reintentar con
    /// backoff) en latencia acumulada para todos.
    /// </summary>
    public int QueueLimit { get; set; }
}
