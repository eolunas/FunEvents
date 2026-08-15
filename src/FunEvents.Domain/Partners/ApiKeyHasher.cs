using System.Security.Cryptography;
using System.Text;

namespace FunEvents.Domain.Partners;

/// <summary>
/// Calcula el hash con el que se almacena y se busca una API Key.
/// </summary>
/// <remarks>
/// <para>
/// Vive en Domain, y no en Api ni en Infrastructure, porque la regla
/// "la clave en claro nunca se persiste" es una invariante del modelo
/// <see cref="Partner"/>, no un detalle del transporte HTTP. Quien emite la
/// clave y quien la valida deben usar exactamente la misma funcion; tenerla
/// duplicada en dos capas es la forma habitual de que dejen de coincidir.
/// </para>
/// <para>
/// <b>Por que SHA-256 y no BCrypt/Argon2.</b> Para una contrasena de persona
/// se usa un hash lento con sal: el espacio de claves es pequeno y predecible,
/// asi que hay que encarecer cada intento. Una API Key es un secreto de 256
/// bits generado por el servidor: no hay diccionario que atacar y un hash lento
/// solo anadiria latencia a cada peticion del colaborador. La propiedad que
/// necesitamos es distinta: que un volcado de la tabla <c>Partners</c> no
/// permita usar ninguna clave.
/// </para>
/// <para>
/// La comparacion posterior se hace en la base de datos por igualdad de hash.
/// No se compara la clave en claro en memoria en ningun punto del flujo.
/// </para>
/// </remarks>
public static class ApiKeyHasher
{
    /// <summary>Devuelve el SHA-256 de la clave en hexadecimal minuscula.</summary>
    public static string Hash(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Genera una clave nueva criptograficamente aleatoria. Es lo que ejecutaria
    /// el endpoint de alta de colaboradores: devuelve la clave en claro <b>una
    /// sola vez</b> y persiste unicamente su hash.
    /// </summary>
    public static (string ApiKey, string Hash) Generate()
    {
        var key = "fev_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return (key, Hash(key));
    }
}
