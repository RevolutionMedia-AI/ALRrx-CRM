namespace ALRrx.Application.DTOs;

public sealed record PeriodComparisonRequestDto
{
    public TimeFilterDto Period1 { get; init; } = new();
    public TimeFilterDto Period2 { get; init; } = new();
}

public sealed record PeriodComparisonResponseDto
{
    public string Period1Label { get; init; } = "";
    public string Period2Label { get; init; } = "";
    public List<KpiRow> Period1Kpis { get; init; } = [];
    public List<KpiRow> Period2Kpis { get; init; } = [];
    public List<KpiRow> KpiChanges { get; init; } = [];
    public List<AgentComparisonRow> Agents { get; init; } = [];
    public List<DispositionRow> Period1Dispositions { get; init; } = [];
    public List<DispositionRow> Period2Dispositions { get; init; } = [];
    public ContactComparison? ContactComparison { get; init; }
}

public sealed class KpiRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string? Trend { get; init; }
    public string Color { get; init; } = "#3B82F6";
}

public sealed class DispositionRow
{
    public string Status { get; init; } = "";
    public string Total { get; init; } = "0";
    public string Percentage { get; init; } = "0";
}

public sealed class AgentComparisonRow
{
    public string Name { get; init; } = "";
    public string User { get; init; } = "";
    public int Period1Calls { get; init; }
    public int Period2Calls { get; init; }
    public int CallsChange { get; init; }
    public double CallsChangePct { get; init; }
    public int Period1Sales { get; init; }
    public int Period2Sales { get; init; }
    public int SalesChange { get; init; }
    public double SalesChangePct { get; init; }
}

public sealed class ContactComparison
{
    public int Period1Contacts { get; init; }
    public int Period2Contacts { get; init; }
    public int Period1NoContacts { get; init; }
    public int Period2NoContacts { get; init; }
    public string Period1Rate { get; init; } = "";
    public string Period2Rate { get; init; } = "";
}