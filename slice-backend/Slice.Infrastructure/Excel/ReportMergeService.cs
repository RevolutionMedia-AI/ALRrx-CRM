using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using System.Drawing;
using System.Text;

namespace Slice.Infrastructure.Excel;

/// <summary>
/// Merges multiple parsed <see cref="SliceReport"/> objects into a single consolidated report
/// and exports it as XLSX and CSV.
/// Merge rules: count/volume fields are <b>summed</b> per key (Pod / Agent+Pod / ShopName);
/// percentage fields are <b>averaged</b>.
/// Export files land in <c>%TEMP%/slice/exports/</c>.
/// </summary>
public sealed class ReportMergeService : IReportMergeService
{
    /// <summary>Background color for XLSX header cells (Slice blue).</summary>
    private static readonly Color HeaderColor = Color.FromArgb(31, 119, 180);

    private readonly ILogger<ReportMergeService> _logger;

    public ReportMergeService(ILogger<ReportMergeService> logger) => _logger = logger;

    /// <inheritdoc/>
    public SliceReport Merge(IEnumerable<SliceReport> reports)
    {
        var list = reports.ToList();

        var merged = new SliceReport
        {
            // ReportDate takes the earliest date found across all source reports.
            ReportDate  = list.Select(r => r.ReportDate).Min(),
            GeneratedAt = DateTime.UtcNow,
        };

        // ── Daily Global: group by Pod, sum volumes, average percentages ──────
        foreach (var grp in list.SelectMany(r => r.DailyGlobal).GroupBy(g => g.Pod))
        {
            var e = grp.ToList();
            merged.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod                 = grp.Key,
                Queued              = e.Sum(x => x.Queued),
                Handled             = e.Sum(x => x.Handled),
                MissedCalls         = e.Sum(x => x.MissedCalls),
                TransferredCalls    = e.Sum(x => x.TransferredCalls),
                PctQueued           = SafeAvg(e, x => x.PctQueued),
                PctHandled          = SafeAvg(e, x => x.PctHandled),
                PctMissed           = SafeAvg(e, x => x.PctMissed),
                PctTransferred      = SafeAvg(e, x => x.PctTransferred),
                ConvPct             = SafeAvg(e, x => x.ConvPct),
                OrderCount          = e.Sum(x => x.OrderCount),
                RefundedOrders      = e.Sum(x => x.RefundedOrders),
                PctOrdersWithErrors = SafeAvg(e, x => x.PctOrdersWithErrors),
            });
        }

        // ── Daily Agents: deduplicate by (AgentEmail, Pod), sum contacts/holds, average times ──
        merged.DailyAgents.AddRange(
            list.SelectMany(r => r.DailyAgents)
                .GroupBy(a => (a.AgentEmail, a.Pod))
                .Select(grp =>
                {
                    var e     = grp.ToList();
                    var first = e[0];
                    return new DailyAgentRow
                    {
                        Pod               = first.Pod,
                        SupervisorName    = first.SupervisorName,
                        AgentEmail        = first.AgentEmail,
                        HC                = e.Sum(x => x.HC),
                        TC                = e.Sum(x => x.TC),
                        NumberOfHolds     = e.Sum(x => x.NumberOfHolds),
                        AvgHoldTime       = SafeAvg(e, x => x.AvgHoldTime),
                        ASA               = SafeAvg(e, x => x.ASA),
                        AHT               = SafeAvg(e, x => x.AHT),
                        ACW               = SafeAvg(e, x => x.ACW),
                        PctContactsOnHold = SafeAvg(e, x => x.PctContactsOnHold),
                        PctSLUnder15Sec   = SafeAvg(e, x => x.PctSLUnder15Sec),
                        PctTransfers      = SafeAvg(e, x => x.PctTransfers),
                        Shift             = first.Shift,
                    };
                }));

        // ── Shop Daily: group by ShopName, sum orders, average rates ──────────
        merged.ShopDaily.AddRange(
            list.SelectMany(r => r.ShopDaily)
                .GroupBy(s => s.ShopName)
                .Select(grp =>
                {
                    var e = grp.ToList();
                    return new ShopDailyRow
                    {
                        ShopName       = grp.Key,
                        TotalOrders    = e.Sum(x => x.TotalOrders),
                        RefundedOrders = e.Sum(x => x.RefundedOrders),
                        ErrorRate      = SafeAvg(e, x => x.ErrorRate),
                        ConversionRate = SafeAvg(e, x => x.ConversionRate),
                    };
                }));

        // ── Shop Call Metrics: keep all (Shop, Pod, WeekStart) rows; sum calls per week, average pcts ──
        merged.ShopCallMetrics.AddRange(
            list.SelectMany(r => r.ShopCallMetrics)
                .GroupBy(s => (s.ShopId, s.PodId, s.WeekStart))
                .Select(grp =>
                {
                    var e = grp.ToList();
                    var first = e[0];
                    return new ShopCallMetricsRow
                    {
                        WeekStart        = grp.Key.WeekStart,
                        ShopId           = grp.Key.ShopId,
                        ShopName         = first.ShopName,
                        PodId            = grp.Key.PodId,
                        TotalCalls       = e.Sum(x => x.TotalCalls),
                        OverflowCalls    = e.Sum(x => x.OverflowCalls),
                        QueueCalls       = e.Sum(x => x.QueueCalls),
                        HandledCalls     = e.Sum(x => x.HandledCalls),
                        MissedCalls      = e.Sum(x => x.MissedCalls),
                        TransferredCalls = e.Sum(x => x.TransferredCalls),
                        PctOverflow      = SafeAvg(e, x => x.PctOverflow),
                        PctQueued        = SafeAvg(e, x => x.PctQueued),
                        PctHandled       = SafeAvg(e, x => x.PctHandled),
                        PctMissedOfQueued= SafeAvg(e, x => x.PctMissedOfQueued),
                        PctTransferred   = SafeAvg(e, x => x.PctTransferred),
                    };
                }));

        return merged;
    }

    /// <inheritdoc/>
    public async Task<string> ExportXlsxAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir   = Path.Combine(Path.GetTempPath(), "slice", "exports");
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Report_{report.Id}.xlsx");

        using var package = new ExcelPackage();
        BuildGlobalSheet(package, report);
        BuildAgentSheet(package, report);
        BuildShopSheet(package, report);
        BuildShopCallMetricsSheet(package, report);

        await package.SaveAsAsync(new FileInfo(filePath), ct);
        _logger.LogInformation("XLSX exported to {Path}", filePath);
        return filePath;
    }

    /// <inheritdoc/>
    public async Task<string> ExportCsvAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir   = Path.Combine(Path.GetTempPath(), "slice", "exports");
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Report_{report.Id}.csv");

        var sb = new StringBuilder();

        sb.AppendLine("=== Daily Global ===");
        sb.AppendLine("Pod,Queued,Handled,Missed Calls,Transferred Calls,%Queued,%Handled,%Missed,%Transferred,Conv %,Order Count,Refunded Orders,% Orders with Errors");
        foreach (var g in report.DailyGlobal)
            sb.AppendLine($"{g.Pod},{g.Queued},{g.Handled},{g.MissedCalls},{g.TransferredCalls}," +
                          $"{g.PctQueued:F2},{g.PctHandled:F2},{g.PctMissed:F2},{g.PctTransferred:F2}," +
                          $"{g.ConvPct:F2},{g.OrderCount},{g.RefundedOrders},{g.PctOrdersWithErrors:F2}");

        sb.AppendLine();
        sb.AppendLine("=== Daily Agent ===");
        sb.AppendLine("Pod,Supervisor,Agent,HC,TC,Holds,Avg Hold Time,ASA,AHT,ACW,% On Hold,%SL<15s,% Transfers,Shift");
        foreach (var a in report.DailyAgents)
            sb.AppendLine($"{a.Pod},{a.SupervisorName},{a.AgentEmail},{a.HC},{a.TC},{a.NumberOfHolds}," +
                          $"{a.AvgHoldTime:F2},{a.ASA:F2},{a.AHT:F2},{a.ACW:F2}," +
                          $"{a.PctContactsOnHold:F2},{a.PctSLUnder15Sec:F2},{a.PctTransfers:F2},{a.Shift}");

        sb.AppendLine();
        sb.AppendLine("=== Shop Daily ===");
        sb.AppendLine("Shop,Total Orders,Refunded Orders,Error Rate,Conversion Rate");
        foreach (var s in report.ShopDaily)
            sb.AppendLine($"{s.ShopName},{s.TotalOrders},{s.RefundedOrders},{s.ErrorRate:F2},{s.ConversionRate:F2}");

        if (report.ShopCallMetrics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== Shop Call Metrics ===");
            sb.AppendLine("Week Start,Shop ID,Shop Name,Pod ID,Total Calls,Overflow,Queued,Handled,Missed,Transferred,%Overflow,%Queued,%Handled,%Missed of Queued,%Transferred");
            foreach (var c in report.ShopCallMetrics)
                sb.AppendLine($"{c.WeekStart:yyyy-MM-dd},{c.ShopId},{c.ShopName},{c.PodId}," +
                              $"{c.TotalCalls},{c.OverflowCalls},{c.QueueCalls},{c.HandledCalls},{c.MissedCalls},{c.TransferredCalls}," +
                              $"{c.PctOverflow:F2},{c.PctQueued:F2},{c.PctHandled:F2},{c.PctMissedOfQueued:F2},{c.PctTransferred:F2}");
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("CSV exported to {Path}", filePath);
        return filePath;
    }

    // ─── XLSX sheet builders ──────────────────────────────────────────────────

    /// <summary>Adds the "Daily Global" worksheet with pod-level metrics.</summary>
    private static void BuildGlobalSheet(ExcelPackage package, SliceReport report)
    {
        var ws      = package.Workbook.Worksheets.Add("Daily Global");
        var headers = new[]
        {
            "Pod", "Queued", "Handled", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%Missed", "%Transferred", "Conv %",
            "Order Count", "Refunded Orders", "% Orders with Errors",
        };
        WriteHeader(ws, 1, headers);

        int row = 2;
        foreach (var g in report.DailyGlobal)
        {
            ws.Cells[row, 1].Value  = g.Pod;
            ws.Cells[row, 2].Value  = g.Queued;
            ws.Cells[row, 3].Value  = g.Handled;
            ws.Cells[row, 4].Value  = g.MissedCalls;
            ws.Cells[row, 5].Value  = g.TransferredCalls;
            ws.Cells[row, 6].Value  = g.PctQueued;
            ws.Cells[row, 7].Value  = g.PctHandled;
            ws.Cells[row, 8].Value  = g.PctMissed;
            ws.Cells[row, 9].Value  = g.PctTransferred;
            ws.Cells[row, 10].Value = g.ConvPct;
            ws.Cells[row, 11].Value = g.OrderCount;
            ws.Cells[row, 12].Value = g.RefundedOrders;
            ws.Cells[row, 13].Value = g.PctOrdersWithErrors;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    /// <summary>Adds the "Daily Agent" worksheet with individual agent metrics.</summary>
    private static void BuildAgentSheet(ExcelPackage package, SliceReport report)
    {
        var ws      = package.Workbook.Worksheets.Add("Daily Agent");
        var headers = new[]
        {
            "Pod", "Supervisor", "Agent", "HC", "TC", "Holds",
            "Avg Hold Time", "ASA", "AHT", "ACW", "% On Hold", "%SL<15s", "% Transfers", "Shift",
        };
        WriteHeader(ws, 1, headers);

        int row = 2;
        foreach (var a in report.DailyAgents)
        {
            ws.Cells[row, 1].Value  = a.Pod;
            ws.Cells[row, 2].Value  = a.SupervisorName;
            ws.Cells[row, 3].Value  = a.AgentEmail;
            ws.Cells[row, 4].Value  = a.HC;
            ws.Cells[row, 5].Value  = a.TC;
            ws.Cells[row, 6].Value  = a.NumberOfHolds;
            ws.Cells[row, 7].Value  = a.AvgHoldTime;
            ws.Cells[row, 8].Value  = a.ASA;
            ws.Cells[row, 9].Value  = a.AHT;
            ws.Cells[row, 10].Value = a.ACW;
            ws.Cells[row, 11].Value = a.PctContactsOnHold;
            ws.Cells[row, 12].Value = a.PctSLUnder15Sec;
            ws.Cells[row, 13].Value = a.PctTransfers;
            ws.Cells[row, 14].Value = a.Shift;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    /// <summary>Adds the "Shop Daily" worksheet. Skipped if the report has no shop rows.</summary>
    private static void BuildShopSheet(ExcelPackage package, SliceReport report)
    {
        if (report.ShopDaily.Count == 0) return;

        var ws = package.Workbook.Worksheets.Add("Shop Daily");
        WriteHeader(ws, 1, ["Shop", "Total Orders", "Refunded Orders", "Error Rate", "Conversion Rate"]);

        int row = 2;
        foreach (var s in report.ShopDaily)
        {
            ws.Cells[row, 1].Value = s.ShopName;
            ws.Cells[row, 2].Value = s.TotalOrders;
            ws.Cells[row, 3].Value = s.RefundedOrders;
            ws.Cells[row, 4].Value = s.ErrorRate;
            ws.Cells[row, 5].Value = s.ConversionRate;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    /// <summary>Adds the "Shop Call Metrics" worksheet. Skipped if empty.</summary>
    private static void BuildShopCallMetricsSheet(ExcelPackage package, SliceReport report)
    {
        if (report.ShopCallMetrics.Count == 0) return;

        var ws = package.Workbook.Worksheets.Add("Shop Call Metrics");
        WriteHeader(ws, 1, [
            "Week Start", "Shop ID", "Shop Name", "Pod ID",
            "Total Calls", "Overflow", "Queued", "Handled", "Missed", "Transferred",
            "%Overflow", "%Queued", "%Handled", "%Missed of Queued", "%Transferred",
        ]);

        int row = 2;
        foreach (var c in report.ShopCallMetrics)
        {
            ws.Cells[row, 1].Value  = c.WeekStart;
            ws.Cells[row, 2].Value  = c.ShopId;
            ws.Cells[row, 3].Value  = c.ShopName;
            ws.Cells[row, 4].Value  = c.PodId;
            ws.Cells[row, 5].Value  = c.TotalCalls;
            ws.Cells[row, 6].Value  = c.OverflowCalls;
            ws.Cells[row, 7].Value  = c.QueueCalls;
            ws.Cells[row, 8].Value  = c.HandledCalls;
            ws.Cells[row, 9].Value  = c.MissedCalls;
            ws.Cells[row, 10].Value = c.TransferredCalls;
            ws.Cells[row, 11].Value = c.PctOverflow;
            ws.Cells[row, 12].Value = c.PctQueued;
            ws.Cells[row, 13].Value = c.PctHandled;
            ws.Cells[row, 14].Value = c.PctMissedOfQueued;
            ws.Cells[row, 15].Value = c.PctTransferred;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    /// <summary>
    /// Writes a styled header row: Slice-blue background, white bold text.
    /// </summary>
    private static void WriteHeader(ExcelWorksheet ws, int row, string[] headers)
    {
        for (int col = 1; col <= headers.Length; col++)
        {
            ws.Cells[row, col].Value = headers[col - 1];
            ws.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, col].Style.Fill.BackgroundColor.SetColor(HeaderColor);
            ws.Cells[row, col].Style.Font.Color.SetColor(Color.White);
            ws.Cells[row, col].Style.Font.Bold = true;
        }
    }

    /// <summary>Returns the average of <paramref name="selector"/> over <paramref name="items"/>, or 0 if the list is empty.</summary>
    private static double SafeAvg<T>(List<T> items, Func<T, double> selector)
        => items.Count == 0 ? 0.0 : items.Average(selector);
}
