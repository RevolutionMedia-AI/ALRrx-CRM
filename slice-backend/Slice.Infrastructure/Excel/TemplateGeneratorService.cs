using OfficeOpenXml;
using OfficeOpenXml.Style;
using Slice.Domain.Entities;
using System.Drawing;

namespace Slice.Infrastructure.Excel;

/// <summary>
/// Builds a downloadable Excel template that mirrors the three report sections
/// (<c>Daily Global</c>, <c>Daily Agent</c>, <c>Shop Daily</c>) expected by
/// <see cref="ExcelParserService"/>. When the source <see cref="SliceReport"/>
/// already has data, the template is pre-populated; otherwise the sheets ship
/// empty so the user can fill them in by hand and re-upload.
/// </summary>
public sealed class TemplateGeneratorService
{
    private static readonly Color HeaderColor = Color.FromArgb(31, 119, 180);

    public string BuildTemplate(SliceReport report, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Template_{report.Id}.xlsx");

        using var package = new ExcelPackage();
        BuildDailyGlobalSheet(package, report.DailyGlobal);
        BuildDailyAgentSheet(package, report.DailyAgents);
        BuildShopDailySheet(package, report.ShopDaily);
        package.SaveAs(new FileInfo(filePath));
        return filePath;
    }

    private static void BuildDailyGlobalSheet(ExcelPackage package, IEnumerable<DailyGlobalRow> rows)
    {
        var ws = package.Workbook.Worksheets.Add("Daily Global");
        WriteHeader(ws, 1, new[]
        {
            "Pod", "Queued", "Handled", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%Missed", "%Transferred", "Conv %",
            "Order Count", "Refunded Orders", "% Orders with Errors",
        });

        int row = 2;
        foreach (var g in rows)
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

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    private static void BuildDailyAgentSheet(ExcelPackage package, IEnumerable<DailyAgentRow> rows)
    {
        var ws = package.Workbook.Worksheets.Add("Daily Agent");
        WriteHeader(ws, 1, new[]
        {
            "POD", "Agent", "HC", "TC", "Holds", "Avg Hold Time",
            "ASA", "AHT", "ACW", "% On Hold", "%SL<15s", "% Transfers", "Shift",
        });

        // Group rows by Pod so the parser can attach the current pod/supervisor
        // to every agent row beneath.
        int row = 2;
        foreach (var podGroup in rows.GroupBy(r => r.Pod))
        {
            // Pod header row.
            ws.Cells[row, 1].Value = "POD";
            ws.Cells[row, 3].Value = podGroup.Key;
            var supervisor = podGroup.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.SupervisorName))?.SupervisorName ?? string.Empty;
            ws.Cells[row, 12].Value = supervisor;
            row++;

            // Agent column-header row.
            ws.Cells[row, 1].Value = "Agent";
            row++;

            foreach (var a in podGroup)
            {
                ws.Cells[row, 1].Value  = a.AgentEmail;
                ws.Cells[row, 2].Value  = a.AgentEmail;
                ws.Cells[row, 3].Value  = a.HC;
                ws.Cells[row, 4].Value  = a.TC;
                ws.Cells[row, 5].Value  = a.NumberOfHolds;
                ws.Cells[row, 6].Value  = a.AvgHoldTime;
                ws.Cells[row, 7].Value  = a.ASA;
                ws.Cells[row, 8].Value  = a.AHT;
                ws.Cells[row, 9].Value  = a.ACW;
                ws.Cells[row, 10].Value = a.PctContactsOnHold;
                ws.Cells[row, 11].Value = a.PctSLUnder15Sec;
                ws.Cells[row, 12].Value = a.PctTransfers;
                ws.Cells[row, 13].Value = a.Shift;
                row++;
            }
        }

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    private static void BuildShopDailySheet(ExcelPackage package, IEnumerable<ShopDailyRow> rows)
    {
        var ws = package.Workbook.Worksheets.Add("Shop Daily");
        WriteHeader(ws, 1, new[]
        {
            "Shop", "Total Orders", "Refunded Orders", "Error Rate", "Conversion Rate",
        });

        int row = 2;
        foreach (var s in rows)
        {
            ws.Cells[row, 1].Value = s.ShopName;
            ws.Cells[row, 2].Value = s.TotalOrders;
            ws.Cells[row, 3].Value = s.RefundedOrders;
            ws.Cells[row, 4].Value = s.ErrorRate;
            ws.Cells[row, 5].Value = s.ConversionRate;
            row++;
        }

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
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
}
