namespace ALRrx.Application.DTOs;

public sealed record DashboardSummaryDto
{
    public List<MetricCardDto> Metrics { get; init; } = [];
    public List<ChartDataDto> Charts { get; init; } = [];
    public DateTime LastUpdated { get; init; }
}

public sealed record MetricCardDto
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Trend { get; init; }
    public string? Format { get; init; }
    public string? Color { get; init; }
}

public sealed record ChartDataDto
{
    public string ChartType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string[] Labels { get; init; } = [];
    public List<ChartSeriesDto> Series { get; init; } = [];
}

public sealed record ChartSeriesDto
{
    public string Name { get; init; } = string.Empty;
    public decimal[] Data { get; init; } = [];
}
