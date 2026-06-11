using ALRrx.Application.Helpers;
using ALRrx.Application.UseCases;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;

namespace ALRrx.Infrastructure.Export;

public sealed class TwilioExcelService : ITwilioExcelService
{
    public string Format => "twilio-excel";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    static TwilioExcelService()
    {
        ExcelPackage.License.SetNonCommercialOrganization("ALRrx CRM");
    }

    public byte[] GenerateTwilioExcel(TwilioExportData data)
    {
        using var package = new ExcelPackage();

        var summarySheet = package.Workbook.Worksheets.Add("Summary");
        WriteSummarySheet(summarySheet, data);

        var dailySheet = package.Workbook.Worksheets.Add("Daily Breakdown");
        WriteDailySheet(dailySheet, data);

        var callsSheet = package.Workbook.Worksheets.Add("Recent Calls");
        WriteRecentCallsSheet(callsSheet, data);

        return package.GetAsByteArray();
    }

    private static void WriteSummarySheet(ExcelWorksheet ws, TwilioExportData data)
    {
        var s = data.Summary;

        ws.Cells[1, 1].Value = "ALRrx — Twilio Costs Report";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[2, 1].Value = $"Period: {data.Period}";
        ws.Cells[2, 1].Style.Font.Italic = true;
        ws.Cells[3, 1].Value = $"Generated: {data.GeneratedAt} {TimeZoneHelper.Label}";
        ws.Cells[3, 1].Style.Font.Color.SetColor(System.Drawing.Color.Gray);

        ws.Cells[5, 1].Value = "Metric";
        ws.Cells[5, 2].Value = "Value";
        using (var range = ws.Cells[5, 1, 5, 2])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        var rows = new (string Label, object Value, string? NumberFormat)[]
        {
            ("Total Spend", (double)s.TotalCost, "$#,##0.0000000"),
            ("Total Calls", s.TotalCalls, null),
            ("Inbound Calls", s.InboundCalls, null),
            ("Outbound Calls", s.OutboundCalls, null),
            ("Total Minutes", s.TotalMinutes, null),
            ("Inbound Cost", (double)s.InboundCost, "$#,##0.0000000"),
            ("Outbound Cost", (double)s.OutboundCost, "$#,##0.0000000"),
            ("Currency", s.Currency, null),
            ("Period Start", TimeZoneHelper.ToPst(s.PeriodStart), "yyyy-mm-dd hh:mm:ss"),
            ("Period End", TimeZoneHelper.ToPst(s.PeriodEnd), "yyyy-mm-dd hh:mm:ss"),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cells[6 + i, 1].Value = rows[i].Label;
            ws.Cells[6 + i, 2].Value = rows[i].Value;
            if (rows[i].NumberFormat != null)
                ws.Cells[6 + i, 2].Style.Numberformat.Format = rows[i].NumberFormat;
        }

        ws.Cells.AutoFitColumns();
    }

    private static void WriteDailySheet(ExcelWorksheet ws, TwilioExportData data)
    {
        var headerRow = 1;
        ws.Cells[headerRow, 1].Value = "Date (PST)";
        ws.Cells[headerRow, 2].Value = "Cost (USD)";
        ws.Cells[headerRow, 3].Value = "Calls";
        ws.Cells[headerRow, 4].Value = "Minutes";

        using (var range = ws.Cells[headerRow, 1, headerRow, 4])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (var i = 0; i < data.Daily.Count; i++)
        {
            var d = data.Daily[i];
            var row = headerRow + 1 + i;
            ws.Cells[row, 1].Value = TimeZoneHelper.ToPst(d.Date);
            ws.Cells[row, 1].Style.Numberformat.Format = "yyyy-mm-dd";
            ws.Cells[row, 2].Value = (double)d.Cost;
            ws.Cells[row, 2].Style.Numberformat.Format = "$#,##0.0000000";
            ws.Cells[row, 3].Value = d.CallCount;
            ws.Cells[row, 4].Value = d.Minutes;
        }

        ws.Cells.AutoFitColumns();

        if (data.Daily.Count > 0)
        {
            var chart = ws.Drawings.AddChart("TwilioDailyCost", eChartType.Line);
            chart.Title.Text = "Daily Twilio Cost";
            chart.SetPosition(1, 0, 6, 0);
            chart.SetSize(520, 320);

            var dataStart = headerRow + 1;
            var dataEnd = headerRow + data.Daily.Count;
            chart.Series.Add(
                ws.Cells[dataStart, 2, dataEnd, 2],
                ws.Cells[dataStart, 1, dataEnd, 1]
            );
            chart.Series[0].HeaderAddress = ws.Cells[headerRow, 2];
        }
    }

    private static void WriteRecentCallsSheet(ExcelWorksheet ws, TwilioExportData data)
    {
        var headerRow = 1;
        var headers = new[] { "Time (PST)", "Direction", "From", "To", "Status", "Duration (s)", "Cost (USD)", "Currency" };
        for (var i = 0; i < headers.Length; i++)
            ws.Cells[headerRow, i + 1].Value = headers[i];

        using (var range = ws.Cells[headerRow, 1, headerRow, headers.Length])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(16, 185, 129));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (var i = 0; i < data.RecentCalls.Count; i++)
        {
            var c = data.RecentCalls[i];
            var row = headerRow + 1 + i;
            ws.Cells[row, 1].Value = TimeZoneHelper.ToPst(c.StartTime);
            ws.Cells[row, 1].Style.Numberformat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cells[row, 2].Value = c.Direction;
            ws.Cells[row, 3].Value = c.From;
            ws.Cells[row, 4].Value = c.To;
            ws.Cells[row, 5].Value = c.Status;
            ws.Cells[row, 6].Value = c.DurationSeconds;
            ws.Cells[row, 7].Value = (double)c.Cost;
            ws.Cells[row, 7].Style.Numberformat.Format = "$#,##0.0000000";
            ws.Cells[row, 8].Value = c.Currency;
        }

        ws.Cells.AutoFitColumns();
    }
}
