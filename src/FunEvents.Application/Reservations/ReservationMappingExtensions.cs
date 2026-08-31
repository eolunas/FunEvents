using FunEvents.Application.Reservations.Dtos;
using FunEvents.Domain.Events;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;

namespace FunEvents.Application.Reservations;

public static class ReservationMappingExtensions
{
    public static ReservationResponse ToResponse(
        this Reservation reservation, Event? @event, User? user, bool previouslyCreated) => new()
    {
        ReservationId = reservation.Id,
        EventId = reservation.EventId,
        EventName = @event?.Name ?? string.Empty,
        UserId = reservation.UserId,
        UserName = user?.FullName ?? string.Empty,
        TicketQuantity = reservation.TicketQuantity,
        State = reservation.State.ToString(),
        Channel = reservation.Channel.ToString(),
        PartnerId = reservation.PartnerId,
        ExpiresAt = reservation.ExpiresAt,
        CreatedAt = reservation.CreatedAt,
        PreviouslyCreated = previouslyCreated
    };
}
