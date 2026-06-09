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
        // Layout alineado con el template "Dashoard Draft (3).xlsx":
        // 3 hojas, cada una con 3 bloques apilados (Global + Agent + Shop).
        BuildPeriodSheet(package, "Daily Report", "Daily Global", "Daily Agent", "Shop Daily", report, period: "daily");
        BuildPeriodSheet(package, "Weekly",       "Weekly Global", "Weekly Agent", "Shop Weekly", report, period: "weekly");
        BuildPeriodSheet(package, "Monthly",      "Monthly Global","Monthly Agent","Shop Monthly", report, period: "monthly");

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

        // ── Bloque Daily Global (también representa Weekly y Monthly con los mismos datos) ──
        sb.AppendLine("=== Daily Global ===");
        sb.AppendLine("Pod,Queued,Handle,Missed Calls,Transferred Calls,%Queued,%Handled,%missed,%Transferred,Conv %,Order Count,Refunded Orders,% Orders with errors");
        foreach (var g in report.DailyGlobal)
            sb.AppendLine($"{g.Pod},{g.Queued},{g.Handled},{g.MissedCalls},{g.TransferredCalls}," +
                          $"{g.PctQueued:F2},{g.PctHandled:F2},{g.PctMissed:F2},{g.PctTransferred:F2}," +
                          $"{g.ConvPct:F2},{g.OrderCount},{g.RefundedOrders},{g.PctOrdersWithErrors:F2}");

        sb.AppendLine();
        sb.AppendLine("=== Daily Agent ===");
        sb.AppendLine("Pod,Supervisor,Agent,HC,TC,Number of Holds,Avg Hold Time,ASA,AHT,ACW,% Contacts on Hold,%SL under 15 sec,% Transfers,Shift");
        foreach (var a in report.DailyAgents)
            sb.AppendLine($"{a.Pod},{a.SupervisorName},{a.AgentEmail},{a.HC},{a.TC},{a.NumberOfHolds}," +
                          $"{a.AvgHoldTime:F2},{a.ASA:F2},{a.AHT:F2},{a.ACW:F2}," +
                          $"{a.PctContactsOnHold:F2},{a.PctSLUnder15Sec:F2},{a.PctTransfers:F2},{a.Shift}");

        sb.AppendLine();
        sb.AppendLine("=== Shop Daily ===");
        sb.AppendLine("Pod - Shops,Shop ID,Total Calls,Overflow,Queued,Handle,Missed Calls,Transferred Calls,%Overflow,%Queued,%Handled,%missed,%Transferred,Order Count,Conv %,Refunded Orders,% Orders with errors");
        var shopRows = FilterShopsByPeriod(report, "daily");
        foreach (var s in shopRows)
            sb.AppendLine($"{s.PodLabel},{s.ShopId},{s.TotalCalls},{s.OverflowCalls},{s.QueueCalls},{s.HandledCalls},{s.MissedCalls},{s.TransferredCalls}," +
                          $"{s.PctOverflow:F2},{s.PctQueued:F2},{s.PctHandled:F2},{s.PctMissed:F2},{s.PctTransferred:F2}," +
                          $"{s.OrderCount},{s.ConversionRate:F2},{s.RefundedOrders},{s.ErrorRate:F2}");

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, ct);
        _logger.LogInformation("CSV exported to {Path}", filePath);
        return filePath;
    }

    // ─── XLSX sheet builders ──────────────────────────────────────────────────

    /// <summary>
    /// Construye una hoja con los 3 bloques apilados (Global + Agent + Shop),
    /// replicando el layout de la plantilla "Dashoard Draft (3).xlsx":
    ///
    ///     <Global Header Title>     (fila N)
    ///     [column headers]           (fila N+1)
    ///     [data rows]
    ///     (fila en blanco)
    ///     <Agent Header Title>       (fila M)
    ///     [POD | <POD_NAME> | Sup | <Supervisor>]  (fila M+1)
    ///     [Agent column headers]     (fila M+2)
    ///     [agent rows]
    ///     (fila en blanco)
    ///     <Shop Header Title>        (fila K)
    ///     [Pod-Shop | Shop ID | ...] (fila K+1)
    ///     [shop rows]
    ///
    /// Por ahora todas las hojas se llenan con los datos diarios del reporte
    /// (DailyGlobal / DailyAgents / ShopCallMetrics). Las hojas Weekly y Monthly
    /// se generan con los mismos datos hasta que el backend agregue ingestión
    /// específica para esos periodos; el layout queda listo para recibirlos.
    /// </summary>
    private static void BuildPeriodSheet(
        ExcelPackage package,
        string sheetName,
        string globalTitle,
        string agentTitle,
        string shopTitle,
        SliceReport report,
        string period)
    {
        var ws = package.Workbook.Worksheets.Add(sheetName);

        // ── Bloque 1: Global ─────────────────────────────────────────────────
        // Fila 1 = título del bloque, Fila 2 = headers, Fila 3+ = datos.
        int row = 1;
        ws.Cells[row, 1].Value = globalTitle;
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        var globalHeaders = new[]
        {
            "Pod", "Queued", "Handle", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%missed", "%Transferred", "Conv %",
            "Order Count", "Refunded  Orders", "% Orders with errors",
        };
        WriteHeader(ws, row, globalHeaders);
        row++;

        var globalRows = FilterByPeriod(report.DailyGlobal, period);
        if (globalRows.Count == 0)
        {
            ws.Cells[row, 1].Value = "(no data for this period)";
            row++;
        }
        else
        {
            foreach (var g in globalRows)
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
        }

        // Fila en blanco de separación.
        row++;

        // ── Bloque 2: Agent (agrupado por POD) ────────────────────────────────
        ws.Cells[row, 1].Value = agentTitle;
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        var agentColumnHeaders = new[]
        {
            "Agent", "HC", "TC", "Number of Holds", "Avg. Hold Time", "ASA",
            "AHT", "ACW", "% Contacts on Hold", "%SL under 15 sec", "% Transfers", "Shift",
        };

        var agentRows = FilterAgentsByPeriod(report.DailyAgents, period);
        if (agentRows.Count == 0)
        {
            ws.Cells[row, 1].Value = "(no data for this period)";
            row++;
        }
        else
        {
            foreach (var podGroup in agentRows.GroupBy(a => a.Pod ?? string.Empty).OrderBy(g => g.Key))
            {
                // Fila de cabecera del POD: B=Pod label, C=POD name, J=Sup label, K=Supervisor
                ws.Cells[row, 2].Value = "POD";
                ws.Cells[row, 3].Value = podGroup.Key;
                ws.Cells[row, 10].Value = "Sup";
                var supervisor = podGroup.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.SupervisorName))?.SupervisorName ?? string.Empty;
                ws.Cells[row, 11].Value = supervisor;
                StylePodHeaderRow(ws.Cells[row, 1, row, 13]);
                row++;

                // Fila de headers de las columnas de agente
                ws.Cells[row, 2].Value = "Agent";
                WriteHeader(ws.Cells[row, 1, row, 13], agentColumnHeaders);
                row++;

                // Filas de agentes (placeholders para Full Time / Part Time si no hay datos)
                if (!podGroup.Any())
                {
                    ws.Cells[row, 2].Value = "agent.name@slice.com";
                    ws.Cells[row, 13].Value = "Full Time";
                    row++;
                    ws.Cells[row, 2].Value = "agent.name@slice.com";
                    ws.Cells[row, 13].Value = "Part Time";
                    row++;
                }
                else
                {
                    foreach (var a in podGroup)
                    {
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
            }
        }

        // Fila en blanco de separación.
        row++;

        // ── Bloque 3: Shop (17 columnas, formato plantilla) ───────────────────
        ws.Cells[row, 2].Value = shopTitle;
        StyleSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        var shopHeaders = new[]
        {
            "Pod - Shops", "Shop ID", "Total Calls", "Overflow", "Queued", "Handle",
            "Missed Calls", "Transferred Calls", "%Overflow", "%Queued", "%Handled",
            "%missed", "%Transferred", "Order Count", "Conv %", "Refunded  Orders",
            "% Orders with errors",
        };
        WriteHeader(ws, row, shopHeaders);
        row++;

        var shopRows = FilterShopsByPeriod(report, period);
        if (shopRows.Count == 0)
        {
            ws.Cells[row, 2].Value = "(no data for this period)";
            row++;
        }
        else
        {
            foreach (var s in shopRows)
            {
                ws.Cells[row, 2].Value  = s.PodLabel;       // B
                ws.Cells[row, 3].Value  = s.ShopId;         // C
                ws.Cells[row, 4].Value  = s.TotalCalls;     // D
                ws.Cells[row, 5].Value  = s.OverflowCalls;  // E
                ws.Cells[row, 6].Value  = s.QueueCalls;     // F
                ws.Cells[row, 7].Value  = s.HandledCalls;   // G
                ws.Cells[row, 8].Value  = s.MissedCalls;    // H
                ws.Cells[row, 9].Value  = s.TransferredCalls;// I
                ws.Cells[row, 10].Value = s.PctOverflow;    // J
                ws.Cells[row, 11].Value = s.PctQueued;      // K
                ws.Cells[row, 12].Value = s.PctHandled;     // L
                ws.Cells[row, 13].Value = s.PctMissed;      // M
                ws.Cells[row, 14].Value = s.PctTransferred; // N
                ws.Cells[row, 15].Value = s.OrderCount;     // O
                ws.Cells[row, 16].Value = s.ConversionRate; // P
                ws.Cells[row, 17].Value = s.RefundedOrders; // Q
                ws.Cells[row, 18].Value = s.ErrorRate;      // R
                row++;
            }
        }

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    private readonly struct ShopRowExport
    {
        public string PodLabel        { get; init; }
        public string ShopId          { get; init; }
        public int    TotalCalls      { get; init; }
        public int    OverflowCalls   { get; init; }
        public int    QueueCalls      { get; init; }
        public int    HandledCalls    { get; init; }
        public int    MissedCalls     { get; init; }
        public int    TransferredCalls{ get; init; }
        public double PctOverflow     { get; init; }
        public double PctQueued       { get; init; }
        public double PctHandled      { get; init; }
        public double PctMissed       { get; init; }
        public double PctTransferred  { get; init; }
        public int    OrderCount      { get; init; }
        public double ConversionRate  { get; init; }
        public int    RefundedOrders  { get; init; }
        public double ErrorRate       { get; init; }
    }

    private static List<DailyGlobalRow> FilterByPeriod(IEnumerable<DailyGlobalRow> rows, string period)
    {
        // Por ahora todos los bloques usan los datos diarios: el backend no
        // separa todavía agregaciones weekly/monthly. Cuando se agregue, este
        // helper será el punto de extensión.
        _ = period;
        return rows.ToList();
    }

    private static List<DailyAgentRow> FilterAgentsByPeriod(IEnumerable<DailyAgentRow> rows, string period)
    {
        _ = period;
        return rows.ToList();
    }

    private static List<ShopRowExport> FilterShopsByPeriod(SliceReport report, string period)
    {
        _ = period;
        // ShopDaily provee Conversion/Refunded/Error/OrderCount pero no métricas
        // de llamadas. ShopCallMetrics provee las métricas de llamadas por
        // (Shop, Pod, Week). Cruzamos ambos por (ShopName) y preferimos la fila
        // de ShopCallMetrics con la WeekStart más reciente cuando hay varias.
        var metricsByShop = report.ShopCallMetrics
            .GroupBy(m => m.ShopName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.WeekStart).First(),
                StringComparer.OrdinalIgnoreCase);

        var result = new List<ShopRowExport>();
        foreach (var s in report.ShopDaily)
        {
            if (metricsByShop.TryGetValue(s.ShopName, out var m))
            {
                result.Add(new ShopRowExport
                {
                    PodLabel        = string.IsNullOrWhiteSpace(m.PodId) ? "Pod" : m.PodId,
                    ShopId          = m.ShopId,
                    TotalCalls      = m.TotalCalls,
                    OverflowCalls   = m.OverflowCalls,
                    QueueCalls      = m.QueueCalls,
                    HandledCalls    = m.HandledCalls,
                    MissedCalls     = m.MissedCalls,
                    TransferredCalls= m.TransferredCalls,
                    PctOverflow     = m.PctOverflow,
                    PctQueued       = m.PctQueued,
                    PctHandled      = m.PctHandled,
                    PctMissed       = m.PctMissedOfQueued,
                    PctTransferred  = m.PctTransferred,
                    OrderCount      = s.TotalOrders,
                    ConversionRate  = s.ConversionRate,
                    RefundedOrders  = s.RefundedOrders,
                    ErrorRate       = s.ErrorRate,
                });
            }
            else
            {
                result.Add(new ShopRowExport
                {
                    PodLabel        = "Pod",
                    ShopId          = string.Empty,
                    OrderCount      = s.TotalOrders,
                    ConversionRate  = s.ConversionRate,
                    RefundedOrders  = s.RefundedOrders,
                    ErrorRate       = s.ErrorRate,
                });
            }
        }
        return result;
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

    /// <summary>Escribe los headers de un bloque que no empieza en la columna A.</summary>
    private static void WriteHeader(ExcelRange range, string[] headers)
    {
        int startRow = range.Start.Row;
        int startCol = range.Start.Column;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = range.Worksheet.Cells[startRow, startCol + i];
            cell.Value = headers[i];
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(HeaderColor);
            cell.Style.Font.Color.SetColor(Color.White);
            cell.Style.Font.Bold = true;
        }
    }

    /// <summary>Estilo del título de bloque (negrita, fondo tenue, sin bordes Slice-blue).</summary>
    private static void StyleSectionTitle(ExcelRange range)
    {
        range.Merge = true;
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 12;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 240, 240));
    }

    /// <summary>Estilo para la fila de cabecera de cada POD dentro del bloque Agent.</summary>
    private static void StylePodHeaderRow(ExcelRange range)
    {
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(HeaderColor);
        range.Style.Font.Color.SetColor(Color.White);
        range.Style.Font.Bold = true;
    }

    /// <summary>Returns the average of <paramref name="selector"/> over <paramref name="items"/>, or 0 if the list is empty.</summary>
    private static double SafeAvg<T>(List<T> items, Func<T, double> selector)
        => items.Count == 0 ? 0.0 : items.Average(selector);
}
