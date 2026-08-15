namespace FunEvents.Domain.Common;

/// <summary>
/// Violacion de una regla de negocio.
/// Lleva un <see cref="ErrorCode"/> estable para que la capa de API pueda
/// mapear a un status HTTP concreto sin inspeccionar el texto del mensaje.
/// </summary>
public class DomainException : Exception
{
    public const string DefaultErrorCode = "DOMAIN_ERROR";

    public string ErrorCode { get; }

    public DomainException(string message, string errorCode = DefaultErrorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public DomainException(string message, Exception innerException, string errorCode = DefaultErrorCode)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
