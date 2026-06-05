namespace ALRrx.Application.DTOs;

public sealed record VicidialLeadDto
{
    public int LeadId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

public enum LeadLookupStatus
{
    Found,
    NotFound,
    ConnectionError,
    InvalidInput
}

public sealed record LeadLookupResult
{
    public LeadLookupStatus Status { get; init; }
    public VicidialLeadDto? Lead { get; init; }
    public string? Message { get; init; }

    public static LeadLookupResult FoundResult(VicidialLeadDto lead) =>
        new() { Status = LeadLookupStatus.Found, Lead = lead };

    public static LeadLookupResult NotFoundResult(int leadId) =>
        new() { Status = LeadLookupStatus.NotFound, Message = $"Lead {leadId} not found in VICIdial" };

    public static LeadLookupResult ConnectionErrorResult(string message) =>
        new() { Status = LeadLookupStatus.ConnectionError, Message = message };

    public static LeadLookupResult InvalidInputResult(string message) =>
        new() { Status = LeadLookupStatus.InvalidInput, Message = message };
}
