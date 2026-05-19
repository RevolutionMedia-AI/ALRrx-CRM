namespace ALRrx.Domain.Entities;

public sealed record QueryDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string SqlTemplate { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
}
