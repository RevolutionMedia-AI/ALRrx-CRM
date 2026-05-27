namespace Slice.Domain.Entities;

public sealed class SliceReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string JobId { get; set; } = string.Empty;
    public DateTime ReportDate { get; set; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public string GeneratedByEmail { get; set; } = string.Empty;
    public List<DailyGlobalRow> DailyGlobal { get; set; } = [];
    public List<DailyAgentRow> DailyAgents { get; set; } = [];
    public List<ShopDailyRow> ShopDaily { get; set; } = [];
    public string? MergedCsvPath { get; set; }
    public string? MergedXlsxPath { get; set; }
}

public sealed class DailyGlobalRow
{
    public string Pod { get; set; } = string.Empty;
    public int Queued { get; set; }
    public int Handled { get; set; }
    public int MissedCalls { get; set; }
    public int TransferredCalls { get; set; }
    public double PctQueued { get; set; }
    public double PctHandled { get; set; }
    public double PctMissed { get; set; }
    public double PctTransferred { get; set; }
    public double ConvPct { get; set; }
    public int OrderCount { get; set; }
    public int RefundedOrders { get; set; }
    public double PctOrdersWithErrors { get; set; }
}

public sealed class DailyAgentRow
{
    public string Pod { get; set; } = string.Empty;
    public string SupervisorName { get; set; } = string.Empty;
    public string AgentEmail { get; set; } = string.Empty;
    public int HC { get; set; }
    public int TC { get; set; }
    public int NumberOfHolds { get; set; }
    public double AvgHoldTime { get; set; }
    public double ASA { get; set; }
    public double AHT { get; set; }
    public double ACW { get; set; }
    public double PctContactsOnHold { get; set; }
    public double PctSLUnder15Sec { get; set; }
    public double PctTransfers { get; set; }
    public string Shift { get; set; } = string.Empty;
}

public sealed class ShopDailyRow
{
    public string ShopName { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public int RefundedOrders { get; set; }
    public double ErrorRate { get; set; }
    public double ConversionRate { get; set; }
}
