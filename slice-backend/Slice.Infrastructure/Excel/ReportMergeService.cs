using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using System.Drawing;
using System.Text;

namespace Slice.Infrastructure.Excel;

public sealed class ReportMergeService : IReportMergeService
{
    private static readonly Color HeaderColor = Color.FromArgb(31, 119, 180);
    private readonly ILogger<ReportMergeService> _logger;

    public ReportMergeService(ILogger<ReportMergeService> logger)
    {
        _logger = logger;
        ExcelPackage.License.SetNonCommercialPersonal("Slice");
    }

    public SliceReport Merge(IEnumerable<SliceReport> reports)
    {
        var list = reports.ToList();
        var merged = new SliceReport
        {
            ReportDate = list.Select(r => r.ReportDate).Min(),
            GeneratedAt = DateTime.UtcNow,
        };

        // Merge DailyGlobal by Pod (sum numeric fields)
        var globalByPod = list
            .SelectMany(r => r.DailyGlobal)
            .GroupBy(g => g.Pod);

        foreach (var grp in globalByPod)
        {
            var entries = grp.ToList();
            merged.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod = grp.Key,
                Queued = entries.Sum(e => e.Queued),
                Handled = entries.Sum(e => e.Handled),
                MissedCalls = entries.Sum(e => e.MissedCalls),
                TransferredCalls = entries.Sum(e => e.TransferredCalls),
                PctQueued = SafeAvg(entries, e => e.PctQueued),
                PctHandled = SafeAvg(entries, e => e.PctHandled),
                PctMissed = SafeAvg(entries, e => e.PctMissed),
                PctTransferred = SafeAvg(entries, e => e.PctTransferred),
                ConvPct = SafeAvg(entries, e => e.ConvPct),
                OrderCount = entries.Sum(e => e.OrderCount),
                RefundedOrders = entries.Sum(e => e.RefundedOrders),
                PctOrdersWithErrors = SafeAvg(entries, e => e.PctOrdersWithErrors),
            });
        }

        // Merge DailyAgents (all rows, deduplicate by email+pod)
        merged.DailyAgents.AddRange(
            list.SelectMany(r => r.DailyAgents)
                .GroupBy(a => (a.AgentEmail, a.Pod))
                .Select(grp =>
                {
                    var entries = grp.ToList();
                    var first = entries[0];
                    return new DailyAgentRow
                    {
                        Pod = first.Pod,
                        SupervisorName = first.SupervisorName,
                        AgentEmail = first.AgentEmail,
                        HC = entries.Sum(e => e.HC),
                        TC = entries.Sum(e => e.TC),
                        NumberOfHolds = entries.Sum(e => e.NumberOfHolds),
                        AvgHoldTime = SafeAvg(entries, e => e.AvgHoldTime),
                        ASA = SafeAvg(entries, e => e.ASA),
                        AHT = SafeAvg(entries, e => e.AHT),
                        ACW = SafeAvg(entries, e => e.ACW),
                        PctContactsOnHold = SafeAvg(entries, e => e.PctContactsOnHold),
                        PctSLUnder15Sec = SafeAvg(entries, e => e.PctSLUnder15Sec),
                        PctTransfers = SafeAvg(entries, e => e.PctTransfers),
                        Shift = first.Shift,
                    };
                }));

        // Merge ShopDaily
        merged.ShopDaily.AddRange(
            list.SelectMany(r => r.ShopDaily)
                .GroupBy(s => s.ShopName)
                .Select(grp =>
                {
                    var entries = grp.ToList();
                    return new ShopDailyRow
                    {
                        ShopName = grp.Key,
                        TotalOrders = entries.Sum(e => e.TotalOrders),
                        RefundedOrders = entries.Sum(e => e.RefundedOrders),
                        ErrorRate = SafeAvg(entries, e => e.ErrorRate),
                        ConversionRate = SafeAvg(entries, e => e.ConversionRate),
                    };
                }));

        return merged;
    }

    public async Task<string> ExportXlsxAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "slice", "exports");
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Report_{report.Id}.xlsx");

        using var package = new ExcelPackage();
        BuildGlobalSheet(package, report);
        BuildAgentSheet(package, report);
        BuildShopSheet(package, report);

        await package.SaveAsAsync(new FileInfo(filePath), ct);
        _logger.LogInformation("XLSX exported to {Path}", filePath);
        return filePath;
    }

    public async Task<string> ExportCsvAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "slice", "exports");
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Report_{report.Id}.csv");

        var sb = new StringBuilder();

        sb.AppendLine("=== Daily Global ===");
        sb.AppendLine("Pod,Queued,Handled,Missed Calls,Transferred Calls,%Queued,%Handled,%Missed,%Transferred,Conv %,Order Count,Refunded Orders,% Orders with Errors");
        foreach (var g in report.DailyGlobal)
            sb.AppendLine($"{g.Pod},{g.Queued},{g.Handled},{g.MissedCalls},{g.TransferredCalls},{g.PctQueued:F2},{g.PctHandled:F2},{g.PctMissed:F2},{g.PctTransferred:F2},{g.ConvPct:F2},{g.OrderCount},{g.RefundedOrders},{g.PctOrdersWithErrors:F2}");

        sb.AppendLine();
        sb.AppendLine("=== Daily Agent ===");
        sb.AppendLine("Pod,Supervisor,Agent,HC,TC,Holds,Avg Hold Time,ASA,AHT,ACW,% On Hold,%SL<15s,% Transfers,Shift");
        foreach (var a in report.DailyAgents)
            sb.AppendLine($"{a.Pod},{a.SupervisorName},{a.AgentEmail},{a.HC},{a.TC},{a.NumberOfHolds},{a.AvgHoldTime:F2},{a.ASA:F2},{a.AHT:F2},{a.ACW:F2},{a.PctContactsOnHold:F2},{a.PctSLUnder15Sec:F2},{a.PctTransfers:F2},{a.Shift}");

        sb.AppendLine();
        sb.AppendLine("=== Shop Daily ===");
        sb.AppendLine("Shop,Total Orders,Refunded Orders,Error Rate,Conversion Rate");
        foreach (var s in report.ShopDaily)
            sb.AppendLine($"{s.ShopName},{s.TotalOrders},{s.RefundedOrders},{s.ErrorRate:F2},{s.ConversionRate:F2}");

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("CSV exported to {Path}", filePath);
        return filePath;
    }

    private static void BuildGlobalSheet(ExcelPackage package, SliceReport report)
    {
        var ws = package.Workbook.Worksheets.Add("Daily Global");
        var headers = new[] { "Pod", "Queued", "Handled", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%Missed", "%Transferred", "Conv %",
            "Order Count", "Refunded Orders", "% Orders with Errors" };

        WriteHeader(ws, 1, headers);

        int row = 2;
        foreach (var g in report.DailyGlobal)
        {
            ws.Cells[row, 1].Value = g.Pod;
            ws.Cells[row, 2].Value = g.Queued;
            ws.Cells[row, 3].Value = g.Handled;
            ws.Cells[row, 4].Value = g.MissedCalls;
            ws.Cells[row, 5].Value = g.TransferredCalls;
            ws.Cells[row, 6].Value = g.PctQueued;
            ws.Cells[row, 7].Value = g.PctHandled;
            ws.Cells[row, 8].Value = g.PctMissed;
            ws.Cells[row, 9].Value = g.PctTransferred;
            ws.Cells[row, 10].Value = g.ConvPct;
            ws.Cells[row, 11].Value = g.OrderCount;
            ws.Cells[row, 12].Value = g.RefundedOrders;
            ws.Cells[row, 13].Value = g.PctOrdersWithErrors;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    private static void BuildAgentSheet(ExcelPackage package, SliceReport report)
    {
        var ws = package.Workbook.Worksheets.Add("Daily Agent");
        var headers = new[] { "Pod", "Supervisor", "Agent", "HC", "TC", "Holds",
            "Avg Hold Time", "ASA", "AHT", "ACW", "% On Hold", "%SL<15s", "% Transfers", "Shift" };

        WriteHeader(ws, 1, headers);

        int row = 2;
        foreach (var a in report.DailyAgents)
        {
            ws.Cells[row, 1].Value = a.Pod;
            ws.Cells[row, 2].Value = a.SupervisorName;
            ws.Cells[row, 3].Value = a.AgentEmail;
            ws.Cells[row, 4].Value = a.HC;
            ws.Cells[row, 5].Value = a.TC;
            ws.Cells[row, 6].Value = a.NumberOfHolds;
            ws.Cells[row, 7].Value = a.AvgHoldTime;
            ws.Cells[row, 8].Value = a.ASA;
            ws.Cells[row, 9].Value = a.AHT;
            ws.Cells[row, 10].Value = a.ACW;
            ws.Cells[row, 11].Value = a.PctContactsOnHold;
            ws.Cells[row, 12].Value = a.PctSLUnder15Sec;
            ws.Cells[row, 13].Value = a.PctTransfers;
            ws.Cells[row, 14].Value = a.Shift;
            row++;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

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

    private static double SafeAvg<T>(List<T> items, Func<T, double> selector)
        => items.Count == 0 ? 0.0 : items.Average(selector);
}
