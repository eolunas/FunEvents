using FunEvents.Application.Events.Dtos;
using FunEvents.Domain.Events;

namespace FunEvents.Application.Events;

public static class EventMappingExtensions
{
    public static EventDto ToDto(this Event e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        Venue = e.Venue,
        StartDate = e.StartDate,
        EndDate = e.EndDate,
        Capacity = e.Capacity,
        ReservedCount = e.ReservedCount,
        AvailableCapacity = e.AvailableCapacity(),
        State = e.State.ToString()
    };
}
