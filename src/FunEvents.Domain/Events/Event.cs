using FunEvents.Domain.Common;

namespace FunEvents.Domain.Events;

/// <summary>
/// Evento a la venta. Es el agregado sobre el que se serializa la concurrencia:
/// <see cref="ReservedCount"/> es el contador atomico que decide si una reserva
/// cabe o no.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no hay una propiedad RowVersion / token de concurrencia optimista.</b>
/// La version anterior declaraba <c>uint RowVersion</c> con <c>IsRowVersion()</c>
/// y nunca la usaba. Con concurrencia optimista, 15 compradores simultaneos
/// producen 14 <c>DbUpdateConcurrencyException</c> y 14 reintentos: el peor
/// comportamiento posible justo en el momento de maxima demanda.
/// </para>
/// <para>
/// La estrategia real es un UPDATE condicional atomico
/// (<c>SET ReservedCount = ReservedCount + n WHERE Capacity - ReservedCount &gt;= n</c>),
/// que resuelve la carrera en el motor con un unico row lock y sin reintentos.
/// Ver ADR-004. Mantener ademas un token de concurrencia sin usar era ruido
/// que sugeria una estrategia que el codigo no aplica.
/// </para>
/// </remarks>
public class Event : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Venue { get; private set; } = string.Empty;
    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset EndDate { get; private set; }
    public int Capacity { get; private set; }
    public int ReservedCount { get; private set; }
    public EventState State { get; private set; }

    /// <summary>Colaborador propietario del evento, si el evento es exclusivo de un partner.</summary>
    public Guid? PartnerId { get; private set; }

    // Requerido por EF Core para materializar desde la base de datos.
    private Event() { }

    public Event(string name, string description, string venue, DateTimeOffset startDate,
        DateTimeOffset endDate, int capacity, Guid? partnerId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Event name is required.", EventErrors.InvalidName);
        if (capacity <= 0)
            throw new DomainException("Capacity must be greater than zero.", EventErrors.InvalidCapacity);
        if (endDate <= startDate)
            throw new DomainException("End date must be after start date.", EventErrors.InvalidDates);

        Name = name;
        Description = description ?? string.Empty;
        Venue = venue ?? string.Empty;
        StartDate = startDate;
        EndDate = endDate;
        Capacity = capacity;
        ReservedCount = 0;
        State = EventState.Draft;
        PartnerId = partnerId;
    }

    public int AvailableCapacity() => Capacity - ReservedCount;

    public bool IsOpenForSale() => State == EventState.Published;

    public void Publish()
    {
        if (State != EventState.Draft)
            throw new DomainException("Only draft events can be published.", EventErrors.InvalidTransition);

        State = EventState.Published;
        Touch();
    }

    public void Cancel()
    {
        if (State == EventState.Completed)
            throw new DomainException("Cannot cancel a completed event.", EventErrors.InvalidTransition);

        State = EventState.Cancelled;
        Touch();
    }

    public void UpdateDetails(string name, string description, string venue,
        DateTimeOffset startDate, DateTimeOffset endDate, int capacity)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Event name is required.", EventErrors.InvalidName);
        if (capacity <= 0)
            throw new DomainException("Capacity must be greater than zero.", EventErrors.InvalidCapacity);
        if (endDate <= startDate)
            throw new DomainException("End date must be after start date.", EventErrors.InvalidDates);
        if (capacity < ReservedCount)
            throw new DomainException(
                $"Capacity cannot be lower than the {ReservedCount} tickets already reserved.",
                EventErrors.InvalidCapacity);

        Name = name;
        Description = description ?? string.Empty;
        Venue = venue ?? string.Empty;
        StartDate = startDate;
        EndDate = endDate;
        Capacity = capacity;
        Touch();
    }
}

public static class EventErrors
{
    public const string InvalidName = "EVENT_INVALID_NAME";
    public const string InvalidCapacity = "EVENT_INVALID_CAPACITY";
    public const string InvalidDates = "EVENT_INVALID_DATES";
    public const string InvalidTransition = "EVENT_INVALID_TRANSITION";
}
