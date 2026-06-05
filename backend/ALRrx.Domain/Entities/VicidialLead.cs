namespace ALRrx.Domain.Entities;

public sealed record VicidialLead
{
    public int LeadId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
