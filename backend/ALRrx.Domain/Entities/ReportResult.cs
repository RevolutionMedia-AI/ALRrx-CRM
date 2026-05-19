namespace ALRrx.Domain.Entities;

public sealed record ReportResult
{
    public string ReportName { get; init; } = string.Empty;
    public string[] Columns { get; init; } = [];
    public Dictionary<string, object?>[] Rows { get; init; } = [];
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public TimeRangeExecuted? TimeRange { get; init; }
}

public sealed record TimeRangeExecuted
{
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
}
