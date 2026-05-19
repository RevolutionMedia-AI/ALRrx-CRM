namespace ALRrx.Domain.Entities;

public sealed record DashboardMetric
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Trend { get; init; }
    public string? Format { get; init; }
    public string? Color { get; init; }
}
