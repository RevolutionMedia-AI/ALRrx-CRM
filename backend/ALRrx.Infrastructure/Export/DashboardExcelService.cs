using ALRrx.Application.UseCases;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;

namespace ALRrx.Infrastructure.Export;

public sealed class DashboardExcelService : IDashboardExcelService
{
    public string Format => "dashboard-excel";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    static DashboardExcelService()
    {
        ExcelPackage.License.SetNonCommercialOrganization("ALRrx CRM");
    }

    public byte[] GenerateDashboardExcel(DashboardPdfData data)
    {
        using var package = new ExcelPackage();

        var kpiSheet = package.Workbook.Worksheets.Add("KPI Summary");
        WriteKpiSheet(kpiSheet, data);

        var dispSheet = package.Workbook.Worksheets.Add("Dispositions");
        WriteDispositionSheet(dispSheet, data);

        var agentSheet = package.Workbook.Worksheets.Add("Agent Performance");
        WriteAgentSheet(agentSheet, data);

        var contactSheet = package.Workbook.Worksheets.Add("Contact Summary");
        WriteContactSheet(contactSheet, data);

        return package.GetAsByteArray();
    }

    private static void WriteKpiSheet(ExcelWorksheet ws, DashboardPdfData data)
    {
        ws.Cells[1, 1].Value = "ALTRX — Operations Report";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[2, 1].Value = $"Period: {data.Period}";
        ws.Cells[2, 1].Style.Font.Italic = true;
        ws.Cells[3, 1].Value = $"Generated: {data.GeneratedAt} UTC";
        ws.Cells[3, 1].Style.Font.Color.SetColor(System.Drawing.Color.Gray);

        ws.Cells[5, 1].Value = "KPI";
        ws.Cells[5, 2].Value = "Value";
        ws.Cells[5, 3].Value = "Trend";
        using (var range = ws.Cells[5, 1, 5, 3])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (var i = 0; i < data.Kpis.Count; i++)
        {
            var kpi = data.Kpis[i];
            ws.Cells[6 + i, 1].Value = kpi.Label;
            ws.Cells[6 + i, 2].Value = kpi.Value;
            ws.Cells[6 + i, 3].Value = kpi.Trend ?? "";
        }

        ws.Cells.AutoFitColumns();
    }

    private static void WriteDispositionSheet(ExcelWorksheet ws, DashboardPdfData data)
    {
        ws.Cells[1, 1].Value = "Disposition";
        ws.Cells[1, 2].Value = "Total";
        ws.Cells[1, 3].Value = "Percentage";
        using (var range = ws.Cells[1, 1, 1, 3])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (var i = 0; i < data.Dispositions.Count; i++)
        {
            var row = data.Dispositions[i];
            ws.Cells[2 + i, 1].Value = row.Status;
            ws.Cells[2 + i, 2].Value = int.TryParse(row.Total, out var t) ? t : row.Total;
            ws.Cells[2 + i, 3].Value = double.TryParse(row.Percentage?.TrimEnd('%'), out var p) ? p / 100 : row.Percentage;
            ws.Cells[2 + i, 3].Style.Numberformat.Format = "0.0%";
        }

        ws.Cells.AutoFitColumns();

        if (data.Dispositions.Count > 0 && data.Dispositions.Count <= 30)
        {
            var chart = ws.Drawings.AddChart("DispositionPie", eChartType.Pie);
            chart.Title.Text = "Call Dispositions";
            chart.SetPosition(1, 0, 5, 0);
            chart.SetSize(420, 320);

            var chartDataStart = 2;
            var chartDataEnd = 1 + data.Dispositions.Count;

            chart.Series.Add(
                ws.Cells[chartDataStart, 2, chartDataEnd, 2],
                ws.Cells[chartDataStart, 1, chartDataEnd, 1]
            );

            chart.PlotArea.Border.Width = 0;
        }
    }

    private static void WriteAgentSheet(ExcelWorksheet ws, DashboardPdfData data)
    {
        ws.Cells[1, 1].Value = "Agent";
        ws.Cells[1, 2].Value = "Calls Handled";
        ws.Cells[1, 3].Value = "Sales Made";
        ws.Cells[1, 4].Value = "Contacts";
        ws.Cells[1, 5].Value = "Conversion %";
        using (var range = ws.Cells[1, 1, 1, 5])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        for (var i = 0; i < data.Agents.Count; i++)
        {
            var a = data.Agents[i];
            ws.Cells[2 + i, 1].Value = a.Name;
            ws.Cells[2 + i, 2].Value = int.TryParse(a.CallsHandled, out var ch) ? ch : a.CallsHandled;
            ws.Cells[2 + i, 3].Value = int.TryParse(a.SalesMade, out var sm) ? sm : a.SalesMade;
            ws.Cells[2 + i, 4].Value = int.TryParse(a.Contacts, out var ct) ? ct : a.Contacts;
            ws.Cells[2 + i, 5].Value = double.TryParse(a.Conversion?.TrimEnd('%'), out var cv) ? cv / 100 : a.Conversion;
            ws.Cells[2 + i, 5].Style.Numberformat.Format = "0.0%";
        }

        ws.Cells.AutoFitColumns();

        if (data.Agents.Count > 0 && data.Agents.Count <= 30)
        {
            var chart = ws.Drawings.AddChart("AgentBar", eChartType.ColumnClustered);
            chart.Title.Text = "Agent Performance — Sales";
            chart.SetPosition(1, 0, 7, 0);
            chart.SetSize(480, 300);

            var dataStart = 2;
            var dataEnd = 1 + data.Agents.Count;

            chart.Series.Add(
                ws.Cells[dataStart, 3, dataEnd, 3],
                ws.Cells[dataStart, 1, dataEnd, 1]
            );
            chart.Series[0].HeaderAddress = ws.Cells[1, 3];
        }
    }

    private static void WriteContactSheet(ExcelWorksheet ws, DashboardPdfData data)
    {
        ws.Cells[1, 1].Value = "Metric";
        ws.Cells[1, 2].Value = "Count";
        using (var range = ws.Cells[1, 1, 1, 2])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
        }

        if (data.ContactData != null)
        {
            ws.Cells[2, 1].Value = "Contacts";
            ws.Cells[2, 2].Value = int.TryParse(data.ContactData.Contacts, out var c) ? c : data.ContactData.Contacts;
            ws.Cells[3, 1].Value = "No Contacts";
            ws.Cells[3, 2].Value = int.TryParse(data.ContactData.NoContacts, out var nc) ? nc : data.ContactData.NoContacts;
            ws.Cells[4, 1].Value = "Contact Rate";
            ws.Cells[4, 2].Value = double.TryParse(data.ContactData.ContactRate?.TrimEnd('%'), out var cr) ? cr / 100 : data.ContactData.ContactRate;
            ws.Cells[4, 2].Style.Numberformat.Format = "0.0%";

            var chart = ws.Drawings.AddChart("ContactPie", eChartType.Pie);
            chart.Title.Text = "Contact vs No Contact";
            chart.SetPosition(1, 0, 4, 0);
            chart.SetSize(380, 280);

            chart.Series.Add(
                ws.Cells[2, 2, 3, 2],
                ws.Cells[2, 1, 3, 1]
            );
        }

        ws.Cells.AutoFitColumns();
    }
}