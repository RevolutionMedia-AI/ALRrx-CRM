namespace Slice.Domain.Entities;

/// <summary>
/// The merged output of one or more processed Slice Excel files.
/// Contains three metric sections (Global, Agent, Shop) and paths to exported files.
/// </summary>
public sealed class SliceReport
{
    /// <summary>Unique identifier (GUID as string) used in API routes.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>ID of the <see cref="ProcessingJob"/> that produced this report.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>The business date the metrics cover (typically today at UTC midnight).</summary>
    public DateTime ReportDate { get; set; }

    /// <summary>UTC timestamp when the merge completed.</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Email of the user who uploaded the source files.</summary>
    public string GeneratedByEmail { get; set; } = string.Empty;

    /// <summary>Per-pod aggregated call-center metrics.</summary>
    public List<DailyGlobalRow> DailyGlobal { get; set; } = [];

    /// <summary>Per-agent call-center metrics, grouped by pod and supervisor.</summary>
    public List<DailyAgentRow> DailyAgents { get; set; } = [];

    /// <summary>Per-shop e-commerce metrics.</summary>
    public List<ShopDailyRow> ShopDaily { get; set; } = [];

    /// <summary>Per-shop, per-week call-center metrics (pivoted layout from shop_level_-_call_metrics.csv).</summary>
    public List<ShopCallMetricsRow> ShopCallMetrics { get; set; } = [];

    /// <summary>Absolute path to the exported CSV file, if available.</summary>
    public string? MergedCsvPath { get; set; }

    /// <summary>Absolute path to the exported XLSX file, if available.</summary>
    public string? MergedXlsxPath { get; set; }
}

/// <summary>Aggregated call-center metrics for a single pod on a given day.</summary>
public sealed class DailyGlobalRow
{
    public string Pod              { get; set; } = string.Empty;
    public int    Queued           { get; set; }
    public int    Handled          { get; set; }
    public int    MissedCalls      { get; set; }
    public int    TransferredCalls { get; set; }
    public double PctQueued        { get; set; }
    public double PctHandled       { get; set; }
    public double PctMissed        { get; set; }
    public double PctTransferred   { get; set; }
    /// <summary>Conversion percentage (contacts → orders).</summary>
    public double ConvPct             { get; set; }
    public int    OrderCount          { get; set; }
    public int    RefundedOrders      { get; set; }
    public double PctOrdersWithErrors { get; set; }
}

/// <summary>Individual agent performance metrics for a single day and shift.</summary>
public sealed class DailyAgentRow
{
    public string Pod            { get; set; } = string.Empty;
    public string SupervisorName { get; set; } = string.Empty;
    public string AgentEmail     { get; set; } = string.Empty;
    /// <summary>Handled Contacts.</summary>
    public int    HC             { get; set; }
    /// <summary>Total Contacts.</summary>
    public int    TC             { get; set; }
    public int    NumberOfHolds  { get; set; }
    public double AvgHoldTime    { get; set; }
    /// <summary>Average Speed of Answer (seconds).</summary>
    public double ASA            { get; set; }
    /// <summary>Average Handle Time (seconds).</summary>
    public double AHT            { get; set; }
    /// <summary>After-Call Work time (seconds).</summary>
    public double ACW            { get; set; }
    public double PctContactsOnHold { get; set; }
    /// <summary>Percentage of contacts answered within 15 seconds (Service Level).</summary>
    public double PctSLUnder15Sec   { get; set; }
    public double PctTransfers      { get; set; }
    public string Shift             { get; set; } = string.Empty;
}

/// <summary>E-commerce order metrics for a single shop on a given day.</summary>
public sealed class ShopDailyRow
{
    public string ShopName       { get; set; } = string.Empty;
    public int    TotalOrders    { get; set; }
    public int    RefundedOrders { get; set; }
    public double ErrorRate      { get; set; }
    public double ConversionRate { get; set; }
}

/// <summary>
/// One week's worth of call-center metrics for a single shop/pod,
/// extracted from the pivoted <c>shop_level_-_call_metrics.csv</c>.
/// </summary>
public sealed class ShopCallMetricsRow
{
    public DateTime WeekStart { get; set; }
    public string   ShopId    { get; set; } = string.Empty;
    public string   ShopName  { get; set; } = string.Empty;
    public string   PodId     { get; set; } = string.Empty;
    public int      TotalCalls         { get; set; }
    public int      OverflowCalls      { get; set; }
    public int      QueueCalls         { get; set; }
    public int      HandledCalls       { get; set; }
    public int      MissedCalls        { get; set; }
    public int      TransferredCalls   { get; set; }
    public double   PctOverflow        { get; set; }
    public double   PctQueued          { get; set; }
    public double   PctHandled         { get; set; }
    public double   PctMissedOfQueued  { get; set; }
    public double   PctTransferred     { get; set; }
}
