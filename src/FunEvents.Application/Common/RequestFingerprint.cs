using System.Security.Cryptography;
using System.Text;

namespace FunEvents.Application.Common;

/// <summary>
/// Huella estable del contenido de una peticion.
/// </summary>
/// <remarks>
/// Se usa junto con la Idempotency-Key para detectar el caso peligroso de
/// reutilizar la misma key con un payload distinto. Sin esta comprobacion, un
/// cliente que reutilice una key por error recibiria un 200 con la reserva de
/// OTRA compra y creeria que su peticion se proceso.
///
/// Se construye a partir de los campos, no del JSON crudo: asi el orden de las
/// propiedades o los espacios en el cuerpo no cambian la huella.
/// </remarks>
public static class RequestFingerprint
{
    public static string Compute(params object?[] parts)
    {
        var canonical = string.Join('|', parts.Select(p => p?.ToString() ?? "\0"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
