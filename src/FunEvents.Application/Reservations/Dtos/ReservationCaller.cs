namespace FunEvents.Application.Reservations.Dtos;

/// <summary>
/// Identidad del llamante, resuelta por el controlador a partir del
/// <c>ClaimsPrincipal</c> y pasada como dato plano para que Application no
/// dependa de ASP.NET.
/// </summary>
public sealed record ReservationCaller(bool IsPartner, bool HasCreateScope, Guid? PartnerId)
{
    public static readonly ReservationCaller Anonymous = new(false, false, null);
}
