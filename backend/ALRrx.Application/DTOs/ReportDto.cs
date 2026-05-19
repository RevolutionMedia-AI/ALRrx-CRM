namespace ALRrx.Application.DTOs;

public sealed record ReportDto
{
    public string ReportName { get; init; } = string.Empty;
    public string[] Columns { get; init; } = [];
    public Dictionary<string, object?>[] Rows { get; init; } = [];
    public DateTime GeneratedAt { get; init; }
    public DateTime TimeRangeStart { get; init; }
    public DateTime TimeRangeEnd { get; init; }
}

public sealed record ExportRequestDto
{
    public string ReportId { get; init; } = string.Empty;
    public string Format { get; init; } = "excel";
    public TimeFilterDto TimeFilter { get; init; } = new();
}
