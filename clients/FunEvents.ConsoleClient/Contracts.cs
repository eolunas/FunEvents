namespace FunEvents.ConsoleClient;

// Contratos declarados del lado del cliente, a proposito.
//
// El cliente NO referencia el proyecto Application: replica los DTOs que
// necesita, exactamente como tendria que hacerlo el portal de un colaborador
// escrito en Java o TypeScript. Si compartiera los tipos del servidor, la demo
// no probaria nada sobre si el contrato REST es autosuficiente, y un cambio
// que rompiese a los integradores externos seguiria compilando aqui.

public record EventDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Venue { get; init; } = string.Empty;
    public DateTimeOffset StartDate { get; init; }
    public int Capacity { get; init; }
    public int ReservedCount { get; init; }
    public int AvailableCapacity { get; init; }
    public string State { get; init; } = string.Empty;
}

public record PagedResponse<T>
{
    public List<T> Items { get; init; } = new();
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages { get; init; }
}

public record UserDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public record AvailabilityResponse
{
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public int TotalCapacity { get; init; }
    public int ReservedCount { get; init; }
    public int AvailableCount { get; init; }
    public bool IsOpenForSale { get; init; }
    public DateTimeOffset AsOf { get; init; }
}

public record ReservationResponse
{
    public Guid ReservationId { get; init; }
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public int TicketQuantity { get; init; }
    public string State { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;

    /// <summary>Colaborador que origino la venta. Lo fija el servidor desde la API Key.</summary>
    public Guid? PartnerId { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool PreviouslyCreated { get; init; }
}

/// <summary>Cuerpo problem+json (RFC 9457) que devuelve la API ante un error.</summary>
public record ProblemDetailsDto
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public int? Status { get; init; }
    public string? Detail { get; init; }
    public string? ErrorCode { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
}

/// <summary>
/// Resultado de una llamada HTTP: o hay valor, o hay problema. Nunca ambos.
/// </summary>
public record ApiResult<T>
{
    public int StatusCode { get; init; }
    public T? Value { get; init; }
    public ProblemDetailsDto? Problem { get; init; }

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Resumen de una linea, listo para imprimir.</summary>
    public string Describe()
    {
        if (IsSuccess) return $"HTTP {StatusCode}";

        var code = Problem?.ErrorCode;
        var detail = Problem?.Detail ?? Problem?.Title;

        if (Problem?.Errors is { Count: > 0 } errors)
            detail = string.Join("; ", errors.SelectMany(e => e.Value));

        return code is null
            ? $"HTTP {StatusCode} - {detail}"
            : $"HTTP {StatusCode} [{code}] - {detail}";
    }
}
