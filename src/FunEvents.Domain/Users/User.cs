using FunEvents.Domain.Common;

namespace FunEvents.Domain.Users;

/// <summary>
/// Comprador. El enunciado pide reservar "a partir de un codigo de evento y
/// de usuario ya conocidos", asi que el usuario es una entidad de primera
/// clase y no un string opaco: permite validar que existe, que esta activo,
/// y aplicar el limite de entradas por usuario y evento.
/// </summary>
public class User : BaseEntity
{
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    // Requerido por EF Core para materializar desde la base de datos.
    private User() { }

    public User(string fullName, string email)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("User full name is required.", UserErrors.InvalidName);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new DomainException("A valid email is required.", UserErrors.InvalidEmail);

        FullName = fullName.Trim();
        Email = email.Trim().ToLowerInvariant();
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }
}

public static class UserErrors
{
    public const string InvalidName = "USER_INVALID_NAME";
    public const string InvalidEmail = "USER_INVALID_EMAIL";
    public const string NotFound = "USER_NOT_FOUND";
    public const string Inactive = "USER_INACTIVE";
}
