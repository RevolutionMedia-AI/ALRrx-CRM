using ALRrx.Application.Helpers;
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;

namespace ALRrx.Infrastructure.Export;

public sealed class PeriodComparisonExcelService : IPeriodComparisonExcelService
{
    public string Format => "period-comparison-excel";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    static PeriodComparisonExcelService()
    {
        ExcelPackage.License.SetNonCommercialOrganization("ALRrx CRM");
    }

    public byte[] GenerateComparisonExcel(PeriodComparisonExcelData data)
    {
        using var package = new ExcelPackage();

        var summarySheet = package.Workbook.Worksheets.Add("Summary & Trends");
        WriteSummarySheet(summarySheet, data);

        var agentSheet = package.Workbook.Worksheets.Add("Agent Performance");
        WriteAgentSheet(agentSheet, data);

        var detailedSheet = package.Workbook.Worksheets.Add("Detailed Data");
        WriteDetailedSheet(detailedSheet, data);

        return package.GetAsByteArray();
    }

    private static void WriteSummarySheet(ExcelWorksheet ws, PeriodComparisonExcelData data)
    {
        ws.Cells[1, 1].Value = "ALTRX — Period Comparison Report";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[2, 1].Value = $"Period 1: {data.Period1Label}";
        ws.Cells[2, 2].Value = $"Period 2: {data.Period2Label}";
        ws.Cells[3, 1].Value = $"Generated: {TimeZoneHelper.NowPstString()} {TimeZoneHelper.Label}";
        ws.Cells[3, 1].Style.Font.Color.SetColor(System.Drawing.Color.Gray);

        var kpiStartRow = 5;
        ws.Cells[kpiStartRow, 1].Value = "KPI";
        ws.Cells[kpiStartRow, 2].Value = data.Period1Label;
        ws.Cells[kpiStartRow, 3].Value = data.Period2Label;
        ws.Cells[kpiStartRow, 4].Value = "Change %";

        using var headerRange = ws.Cells[kpiStartRow, 1, kpiStartRow, 4];
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
        headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);

        for (var i = 0; i < data.Period1Kpis.Count; i++)
        {
            var row = kpiStartRow + 1 + i;
            var p1 = data.Period1Kpis[i];
            var p2 = data.Period2Kpis.Count > i ? data.Period2Kpis[i] : new KpiRow { Value = "0" };
            var change = data.KpiChanges.Count > i ? data.KpiChanges[i] : new KpiRow { Value = "0%" };

            ws.Cells[row, 1].Value = p1.Label;
            ws.Cells[row, 2].Value = p1.Value;
            ws.Cells[row, 3].Value = p2.Value;
            ws.Cells[row, 4].Value = change.Value;

            var changeColor = change.Value.StartsWith("+") ? System.Drawing.Color.FromArgb(16, 185, 129) : System.Drawing.Color.FromArgb(225, 29, 72);
            ws.Cells[row, 4].Style.Font.Color.SetColor(changeColor);
            ws.Cells[row, 4].Style.Font.Bold = true;
        }

        var chartRow = kpiStartRow + data.Period1Kpis.Count + 3;
        ws.Cells[chartRow, 1].Value = "KPI Comparison Chart";
        ws.Cells[chartRow, 1].Style.Font.Bold = true;
        ws.Cells[chartRow, 1].Style.Font.Size = 12;

        var dataStartRow = chartRow + 2;
        ws.Cells[dataStartRow, 1].Value = "Period";
        ws.Cells[dataStartRow, 2].Value = "Sales period";
        ws.Cells[dataStartRow, 3].Value = "Contacts";
        ws.Cells[dataStartRow, 4].Value = "Total Calls";

        using var chartHeader = ws.Cells[dataStartRow, 1, dataStartRow, 4];
        chartHeader.Style.Font.Bold = true;
        chartHeader.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        chartHeader.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(99, 102, 241));
        chartHeader.Style.Font.Color.SetColor(System.Drawing.Color.White);

        ws.Cells[dataStartRow + 1, 1].Value = data.Period1Label;
        ws.Cells[dataStartRow + 1, 2].Value = ParseKpiNumeric(GetKpiValue(data.Period1Kpis, "Sales Today"));
        ws.Cells[dataStartRow + 1, 3].Value = ParseKpiNumeric(GetKpiValue(data.Period1Kpis, "Contacts"));
        ws.Cells[dataStartRow + 1, 4].Value = ParseKpiNumeric(GetKpiValue(data.Period1Kpis, "Total Calls"));

        ws.Cells[dataStartRow + 2, 1].Value = data.Period2Label;
        ws.Cells[dataStartRow + 2, 2].Value = ParseKpiNumeric(GetKpiValue(data.Period2Kpis, "Sales Today"));
        ws.Cells[dataStartRow + 2, 3].Value = ParseKpiNumeric(GetKpiValue(data.Period2Kpis, "Contacts"));
        ws.Cells[dataStartRow + 2, 4].Value = ParseKpiNumeric(GetKpiValue(data.Period2Kpis, "Total Calls"));

        var chart = ws.Drawings.AddChart("KpiComparisonBar", eChartType.ColumnClustered);
        chart.Title.Text = "KPI Comparison by Period";
        chart.SetPosition(chartRow + 5, 0, 0, 0);
        chart.SetSize(700, 350);

        var s1 = chart.Series.Add(
            ws.Cells[dataStartRow + 1, 2, dataStartRow + 2, 2],
            ws.Cells[dataStartRow + 1, 1, dataStartRow + 2, 1]);
        s1.HeaderAddress = ws.Cells[dataStartRow, 2];

        var s2 = chart.Series.Add(
            ws.Cells[dataStartRow + 1, 3, dataStartRow + 2, 3],
            ws.Cells[dataStartRow + 1, 1, dataStartRow + 2, 1]);
        s2.HeaderAddress = ws.Cells[dataStartRow, 3];

        var s3 = chart.Series.Add(
            ws.Cells[dataStartRow + 1, 4, dataStartRow + 2, 4],
            ws.Cells[dataStartRow + 1, 1, dataStartRow + 2, 1]);
        s3.HeaderAddress = ws.Cells[dataStartRow, 4];

        var changeRow = dataStartRow + 5;
        ws.Cells[changeRow, 1].Value = "% Change Summary";
        ws.Cells[changeRow, 1].Style.Font.Bold = true;
        ws.Cells[changeRow, 1].Style.Font.Size = 12;

        ws.Cells[changeRow + 1, 1].Value = "Metric";
        ws.Cells[changeRow + 1, 2].Value = "Change %";
        using var changeHeader = ws.Cells[changeRow + 1, 1, changeRow + 1, 2];
        changeHeader.Style.Font.Bold = true;
        changeHeader.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        changeHeader.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(16, 185, 129));
        changeHeader.Style.Font.Color.SetColor(System.Drawing.Color.White);

        for (var i = 0; i < data.KpiChanges.Count; i++)
        {
            var changeData = data.KpiChanges[i];
            var rowNum = changeRow + 2 + i;
            ws.Cells[rowNum, 1].Value = changeData.Label;
            ws.Cells[rowNum, 2].Value = changeData.Value;
            var isPositive = changeData.Value.StartsWith("+");
            ws.Cells[rowNum, 2].Style.Font.Color.SetColor(isPositive ? System.Drawing.Color.FromArgb(16, 185, 129) : System.Drawing.Color.FromArgb(225, 29, 72));
            ws.Cells[rowNum, 2].Style.Font.Bold = true;
        }

        ws.Cells.AutoFitColumns();
    }

    private static void WriteAgentSheet(ExcelWorksheet ws, PeriodComparisonExcelData data)
    {
        ws.Cells[1, 1].Value = "ALTRX — Agent Performance Comparison";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[2, 1].Value = $"Period 1: {data.Period1Label} vs Period 2: {data.Period2Label}";

        var headerRow = 4;
        ws.Cells[headerRow, 1].Value = "Agent";
        ws.Cells[headerRow, 2].Value = $"{data.Period1Label} Calls";
        ws.Cells[headerRow, 3].Value = $"{data.Period2Label} Calls";
        ws.Cells[headerRow, 4].Value = "Calls Change %";
        ws.Cells[headerRow, 5].Value = $"{data.Period1Label} Sales";
        ws.Cells[headerRow, 6].Value = $"{data.Period2Label} Sales";
        ws.Cells[headerRow, 7].Value = "Sales Change %";

        using var headerRange = ws.Cells[headerRow, 1, headerRow, 7];
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        headerRange.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
        headerRange.Style.Font.Color.SetColor(System.Drawing.Color.White);

        for (var i = 0; i < data.Agents.Count; i++)
        {
            var agent = data.Agents[i];
            var row = headerRow + 1 + i;
            ws.Cells[row, 1].Value = agent.Name;
            ws.Cells[row, 2].Value = agent.Period1Calls;
            ws.Cells[row, 3].Value = agent.Period2Calls;
            ws.Cells[row, 4].Value = agent.CallsChangePct != 0 ? $"{agent.CallsChangePct:F1}%" : "0%";
            ws.Cells[row, 5].Value = agent.Period1Sales;
            ws.Cells[row, 6].Value = agent.Period2Sales;
            ws.Cells[row, 7].Value = agent.SalesChangePct != 0 ? $"{agent.SalesChangePct:F1}%" : "0%";

            var callsColor = agent.CallsChangePct >= 0 ? System.Drawing.Color.FromArgb(16, 185, 129) : System.Drawing.Color.FromArgb(225, 29, 72);
            var salesColor = agent.SalesChangePct >= 0 ? System.Drawing.Color.FromArgb(16, 185, 129) : System.Drawing.Color.FromArgb(225, 29, 72);
            ws.Cells[row, 4].Style.Font.Color.SetColor(callsColor);
            ws.Cells[row, 7].Style.Font.Color.SetColor(salesColor);
        }

        if (data.Agents.Count > 0 && data.Agents.Count <= 30)
        {
            var chart = ws.Drawings.AddChart("AgentSalesComparison", eChartType.ColumnClustered);
            chart.Title.Text = "Sales Comparison by Agent";
            chart.SetPosition(headerRow + data.Agents.Count + 3, 0, 0, 0);
            chart.SetSize(700, 350);

            var dataStart = headerRow + 1;
            var dataEnd = headerRow + data.Agents.Count;

            chart.Series.Add(ws.Cells[dataStart, 5, dataEnd, 5], ws.Cells[dataStart, 1, dataEnd, 1]);
            chart.Series[0].HeaderAddress = ws.Cells[headerRow, 5];
            chart.Series.Add(ws.Cells[dataStart, 6, dataEnd, 6], ws.Cells[dataStart, 1, dataEnd, 1]);
            chart.Series[1].HeaderAddress = ws.Cells[headerRow, 6];
        }

        ws.Cells.AutoFitColumns();
    }

    private static void WriteDetailedSheet(ExcelWorksheet ws, PeriodComparisonExcelData data)
    {
        ws.Cells[1, 1].Value = "ALTRX — Detailed Data Comparison";
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.Font.Size = 14;
        ws.Cells[2, 1].Value = $"Period 1: {data.Period1Label} | Period 2: {data.Period2Label}";

        var currentRow = 4;

        ws.Cells[currentRow, 1].Value = "Dispositions";
        ws.Cells[currentRow, 1].Style.Font.Bold = true;
        ws.Cells[currentRow, 1].Style.Font.Size = 12;
        currentRow++;

        ws.Cells[currentRow, 1].Value = "Disposition";
        ws.Cells[currentRow, 2].Value = $"{data.Period1Label} Total";
        ws.Cells[currentRow, 3].Value = $"{data.Period2Label} Total";
        ws.Cells[currentRow, 4].Value = "%";

        using var dispHeader = ws.Cells[currentRow, 1, currentRow, 4];
        dispHeader.Style.Font.Bold = true;
        dispHeader.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
        dispHeader.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(139, 92, 246));
        dispHeader.Style.Font.Color.SetColor(System.Drawing.Color.White);

        currentRow++;
        var maxDispositions = Math.Max(data.Period1Dispositions.Count, data.Period2Dispositions.Count);
        for (var i = 0; i < maxDispositions; i++)
        {
            var d1 = data.Period1Dispositions.Count > i ? data.Period1Dispositions[i] : null;
            var d2 = data.Period2Dispositions.Count > i ? data.Period2Dispositions[i] : null;
            ws.Cells[currentRow + i, 1].Value = (d1?.Status ?? d2?.Status) ?? "";
            ws.Cells[currentRow + i, 2].Value = d1?.Total ?? "0";
            ws.Cells[currentRow + i, 3].Value = d2?.Total ?? "0";
            ws.Cells[currentRow + i, 4].Value = (d1?.Percentage ?? d2?.Percentage) ?? "0%";
        }
        currentRow += maxDispositions + 2;

        ws.Cells[currentRow, 1].Value = "Contact vs No Contact";
        ws.Cells[currentRow, 1].Style.Font.Bold = true;
        ws.Cells[currentRow, 1].Style.Font.Size = 12;
        currentRow++;

        if (data.ContactComparison != null)
        {
            ws.Cells[currentRow, 1].Value = "Metric";
            ws.Cells[currentRow, 2].Value = data.Period1Label;
            ws.Cells[currentRow, 3].Value = data.Period2Label;
            using var contactHeader = ws.Cells[currentRow, 1, currentRow, 3];
            contactHeader.Style.Font.Bold = true;
            contactHeader.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            contactHeader.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(16, 185, 129));
            contactHeader.Style.Font.Color.SetColor(System.Drawing.Color.White);

            currentRow++;
            ws.Cells[currentRow, 1].Value = "Contacts";
            ws.Cells[currentRow, 2].Value = data.ContactComparison.Period1Contacts;
            ws.Cells[currentRow, 3].Value = data.ContactComparison.Period2Contacts;
            currentRow++;
            ws.Cells[currentRow, 1].Value = "No Contacts";
            ws.Cells[currentRow, 2].Value = data.ContactComparison.Period1NoContacts;
            ws.Cells[currentRow, 3].Value = data.ContactComparison.Period2NoContacts;
            currentRow++;
            ws.Cells[currentRow, 1].Value = "Contact Rate";
            ws.Cells[currentRow, 2].Value = data.ContactComparison.Period1Rate;
            ws.Cells[currentRow, 3].Value = data.ContactComparison.Period2Rate;

            var contactsRow = currentRow - 2;
            var noContactsRow = currentRow - 1;
            var chart = ws.Drawings.AddChart("ContactComparison", eChartType.ColumnClustered);
            chart.Title.Text = "Contact vs No Contact by Period";
            chart.SetPosition(currentRow + 3, 0, 0, 0);
            chart.SetSize(500, 300);

            chart.Series.Add(ws.Cells[contactsRow, 2, noContactsRow, 2], ws.Cells[contactsRow, 1, noContactsRow, 1]);
            chart.Series[0].HeaderAddress = ws.Cells[contactsRow - 1, 2];
            chart.Series.Add(ws.Cells[contactsRow, 3, noContactsRow, 3], ws.Cells[contactsRow, 1, noContactsRow, 1]);
            chart.Series[1].HeaderAddress = ws.Cells[contactsRow - 1, 3];
        }

        ws.Cells.AutoFitColumns();
    }

    private static string GetKpiValue(List<KpiRow> kpis, string label)
    {
        return kpis.FirstOrDefault(k => k.Label == label)?.Value ?? "0";
    }

    private static double ParseKpiNumeric(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var cleaned = value.Replace("%", "").Replace(",", "").Trim();
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
    }
}

public interface IPeriodComparisonExcelService
{
    string Format { get; }
    string ContentType { get; }
    byte[] GenerateComparisonExcel(PeriodComparisonExcelData data);
}

public sealed class PeriodComparisonExcelData
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

public sealed class AgentComparisonRow
{
    public string Name { get; init; } = "";
    public string User { get; init; } = "";
    public int Period1Calls { get; init; }
    public int Period2Calls { get; init; }
    public double CallsChangePct { get; init; }
    public int Period1Sales { get; init; }
    public int Period2Sales { get; init; }
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

public sealed class KpiRow
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Color { get; init; } = "#3B82F6";
}

public sealed class DispositionRow
{
    public string Status { get; init; } = "";
    public string Total { get; init; } = "0";
    public string Percentage { get; init; } = "0";
}