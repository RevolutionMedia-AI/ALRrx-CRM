using System.ComponentModel.DataAnnotations.Schema;

namespace Slice.Infrastructure.Persistence;

/// <summary>
/// EF-mapped root entity for the merged report. Mirrors the domain
/// <c>SliceReport</c> but adds an integer surrogate key for the DB.
/// </summary>
public sealed class SliceReportEntity
{
    public string Id { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string GeneratedByEmail { get; set; } = string.Empty;
    public string? MergedCsvPath { get; set; }
    public string? MergedXlsxPath { get; set; }

    public List<DailyGlobalEntity> DailyGlobal { get; set; } = [];
    public List<DailyAgentEntity> DailyAgents { get; set; } = [];
    public List<ShopDailyEntity> ShopDaily { get; set; } = [];
    public List<ShopCallMetricsEntity> ShopCallMetrics { get; set; } = [];
}

public sealed class DailyGlobalEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string ReportId { get; set; } = string.Empty;
    public SliceReportEntity? Report { get; set; }

    public string Pod { get; set; } = string.Empty;
    public int    Queued           { get; set; }
    public int    Handled          { get; set; }
    public int    MissedCalls      { get; set; }
    public int    TransferredCalls { get; set; }
    public double PctQueued        { get; set; }
    public double PctHandled       { get; set; }
    public double PctMissed        { get; set; }
    public double PctTransferred   { get; set; }
    public double ConvPct             { get; set; }
    public int    OrderCount          { get; set; }
    public int    RefundedOrders      { get; set; }
    public double PctOrdersWithErrors { get; set; }
}

public sealed class DailyAgentEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string ReportId { get; set; } = string.Empty;
    public SliceReportEntity? Report { get; set; }

    public string Pod            { get; set; } = string.Empty;
    public string SupervisorName { get; set; } = string.Empty;
    public string AgentEmail     { get; set; } = string.Empty;
    public int    HC             { get; set; }
    public int    TC             { get; set; }
    public int    NumberOfHolds  { get; set; }
    public double AvgHoldTime    { get; set; }
    public double ASA            { get; set; }
    public double AHT            { get; set; }
    public double ACW            { get; set; }
    public double PctContactsOnHold { get; set; }
    public double PctSLUnder15Sec   { get; set; }
    public double PctTransfers      { get; set; }
    public string Shift             { get; set; } = string.Empty;
}

public sealed class ShopDailyEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string ReportId { get; set; } = string.Empty;
    public SliceReportEntity? Report { get; set; }

    public string ShopName       { get; set; } = string.Empty;
    public string ShopId         { get; set; } = string.Empty;
    public int    TotalOrders    { get; set; }
    public int    RefundedOrders { get; set; }
    public double ErrorRate      { get; set; }
    public double ConversionRate { get; set; }
}

public sealed class ShopCallMetricsEntity
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string ReportId { get; set; } = string.Empty;
    public SliceReportEntity? Report { get; set; }

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

public sealed class ProcessingJobEntity
{
    public string Id { get; set; } = string.Empty;
    public string? ReportId { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CreatedByEmail { get; set; } = string.Empty;
}
