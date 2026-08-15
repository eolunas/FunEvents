using FluentValidation;
using FunEvents.Application.Availability;
using FunEvents.Application.Events;
using FunEvents.Application.Reservations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FunEvents.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<Users.IUserService, Users.UserService>();

        // Reloj inyectable en lugar de DateTimeOffset.UtcNow disperso por el
        // codigo: permite que los tests adelanten el tiempo para comprobar la
        // caducidad sin esperar la ventana real de retencion.
        services.TryAddSingleton(TimeProvider.System);

        services.AddValidatorsFromAssemblyContaining<Reservations.Dtos.CreateReservationRequestValidator>();

        return services;
    }
}
