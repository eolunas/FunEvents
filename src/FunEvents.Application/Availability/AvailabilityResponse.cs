namespace FunEvents.Application.Availability;

public record AvailabilityResponse
{
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public int TotalCapacity { get; init; }
    public int ReservedCount { get; init; }
    public int AvailableCount { get; init; }
    public bool IsOpenForSale { get; init; }

    /// <summary>
    /// Momento en que se calculo. La disponibilidad cambia constantemente bajo
    /// demanda alta; exponer la marca de tiempo evita que un cliente trate este
    /// numero como una garantia de compra.
    /// </summary>
    public DateTimeOffset AsOf { get; init; }
}
