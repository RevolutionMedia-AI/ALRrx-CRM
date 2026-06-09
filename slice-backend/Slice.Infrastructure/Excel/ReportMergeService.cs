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
        // Una sola hoja "Snapshot" con los 3 bloques apilados (Global + Agent + Shop).
        // El reporte actual no segrega por periodicidad (el parser popula DailyGlobal
        // y DailyAgents a partir de cualquier .zip que matchee, mezclando granularidades),
        // asi que no tiene sentido generar 3 hojas Daily/Weekly/Monthly con datos que
        // pueden no corresponder al periodo. Mostramos un snapshot honesto del estado
        // actual con placeholders cuando un bloque no tiene datos.
        BuildSnapshotSheet(package, report);

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

        // ── Bloque Global ─────────────────────────────────────────────────────
        sb.AppendLine("=== Global ===");
        sb.AppendLine("Pod,Queued,Handle,Missed Calls,Transferred Calls,%Queued,%Handled,%missed,%Transferred,Conv %,Order Count,Refunded Orders,% Orders with errors");
        var globalRows = report.DailyGlobal.Count > 0
            ? report.DailyGlobal.ToList()
            : GetPlaceholderGlobalRows();
        foreach (var g in globalRows)
            sb.AppendLine($"{g.Pod},{g.Queued},{g.Handled},{g.MissedCalls},{g.TransferredCalls}," +
                          $"{g.PctQueued:F2},{g.PctHandled:F2},{g.PctMissed:F2},{g.PctTransferred:F2}," +
                          $"{g.ConvPct:F2},{g.OrderCount},{g.RefundedOrders},{g.PctOrdersWithErrors:F2}");

        sb.AppendLine();
        // ── Bloque Agent ──────────────────────────────────────────────────────
        sb.AppendLine("=== Agent ===");
        sb.AppendLine("Pod,Supervisor,Agent,HC,TC,Number of Holds,Avg Hold Time,ASA,AHT,ACW,% Contacts on Hold,%SL under 15 sec,% Transfers,Shift");
        var agentRows = report.DailyAgents.Count > 0
            ? report.DailyAgents
            : GetPlaceholderAgentRows();
        foreach (var a in agentRows)
            sb.AppendLine($"{a.Pod},{a.SupervisorName},{a.AgentEmail},{a.HC},{a.TC},{a.NumberOfHolds}," +
                          $"{a.AvgHoldTime:F2},{a.ASA:F2},{a.AHT:F2},{a.ACW:F2}," +
                          $"{a.PctContactsOnHold:F2},{a.PctSLUnder15Sec:F2},{a.PctTransfers:F2},{a.Shift}");

        sb.AppendLine();
        // ── Bloque Shop ───────────────────────────────────────────────────────
        sb.AppendLine("=== Shop ===");
        sb.AppendLine("Pod - Shops,Shop ID,Total Calls,Overflow,Queued,Handle,Missed Calls,Transferred Calls,%Overflow,%Queued,%Handled,%missed,%Transferred,Order Count,Conv %,Refunded Orders,% Orders with errors");
        var shopRows = BuildShopRowsForExport(report);
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
    /// PODs tipicos que se muestran como placeholder cuando un reporte no tiene
    /// datos en el bloque Global. Coinciden con los del template "Dashoard
    /// Draft (3).xlsx" para que el reporte exportado sea visualmente consistente
    /// con la plantilla en blanco.
    /// </summary>
    private static readonly string[] DefaultPodPlaceholders = { "ES-12", "ES-16", "ES-17", "ES-18" };

    /// <summary>
    /// Construye una unica hoja "Slice Report (Snapshot)" con los 3 bloques
    /// apilados (Global + Agent + Shop), replicando el layout de la plantilla
    /// "Dashoard Draft (3).xlsx". Cuando un bloque no tiene datos, muestra los
    /// placeholders del template (no "(no data)") para que el reporte sea
    /// estructuralmente consistente con la plantilla.
    ///
    ///     <Global>                    (fila 1)
    ///     [column headers]             (fila 2)
    ///     [data rows]
    ///     (fila en blanco)
    ///     <Agent>                      (fila N)
    ///     [POD | <name> | Sup | <sup>] (fila N+1)
    ///     [Agent column headers]       (fila N+2)   ← headers en B-M (12 cols)
    ///     [agent rows]                 ← datos en B-M, columna A vacia como margen
    ///     (fila en blanco)
    ///     <Shop>                       (fila K)
    ///     [Pod-Shop | Shop ID | ...]   (fila K+1)   ← 17 columnas B-R
    ///     [shop rows]
    /// </summary>
    private static void BuildSnapshotSheet(ExcelPackage package, SliceReport report)
    {
        var ws = package.Workbook.Worksheets.Add("Slice Report (Snapshot)");

        int row = 1;
        WriteGlobalBlock(ws, ref row, report);
        row++; // separador
        WriteAgentBlock(ws, ref row, report);
        row++; // separador
        WriteShopBlock(ws, ref row, report);

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
    }

    /// <summary>Bloque Global: 1 fila de titulo, 1 fila de headers, N filas de datos (o placeholders).</summary>
    private static void WriteGlobalBlock(ExcelWorksheet ws, ref int row, SliceReport report)
    {
        ws.Cells[row, 1].Value = "Global";
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        WriteHeader(ws, row, new[]
        {
            "Pod", "Queued", "Handle", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%missed", "%Transferred", "Conv %",
            "Order Count", "Refunded  Orders", "% Orders with errors",
        });
        row++;

        var globalRows = report.DailyGlobal.Count > 0
            ? report.DailyGlobal.ToList()
            : GetPlaceholderGlobalRows();

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

    /// <summary>
    /// Bloque Agent: por cada POD, 1 fila de cabecera (B/C + J/K), 1 fila de
    /// headers (B-M, 12 cols), N filas de agentes. Columna A queda vacia como
    /// margen (igual que el template original). Si el reporte no tiene agentes,
    /// genera los placeholders para los PODs tipicos.
    /// </summary>
    private static void WriteAgentBlock(ExcelWorksheet ws, ref int row, SliceReport report)
    {
        ws.Cells[row, 1].Value = "Agent";
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        var agentColumnHeaders = new[]
        {
            "Agent", "HC", "TC", "Number of Holds", "Avg. Hold Time", "ASA",
            "AHT", "ACW", "% Contacts on Hold", "%SL under 15 sec", "% Transfers", "Shift",
        };

        var agentRows = report.DailyAgents.Count > 0
            ? report.DailyAgents
            : GetPlaceholderAgentRows();

        foreach (var podGroup in agentRows.GroupBy(a => a.Pod ?? string.Empty).OrderBy(g => g.Key))
        {
            // Fila de cabecera del POD: B="POD", C=nombre del POD, J="Sup", K=supervisor
            ws.Cells[row, 2].Value = "POD";
            ws.Cells[row, 3].Value = podGroup.Key;
            ws.Cells[row, 10].Value = "Sup";
            var supervisor = podGroup.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.SupervisorName))?.SupervisorName ?? string.Empty;
            ws.Cells[row, 11].Value = supervisor;
            StylePodHeaderRow(ws.Cells[row, 1, row, 13]);
            row++;

            // Fila de headers de las columnas de agente (rango B-M, 12 columnas).
            WriteHeader(ws.Cells[row, 2, row, 13], agentColumnHeaders);
            row++;

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

    /// <summary>Bloque Shop: titulo en B, headers en B-R (17 cols), datos en B-R. Columna A vacia.</summary>
    private static void WriteShopBlock(ExcelWorksheet ws, ref int row, SliceReport report)
    {
        ws.Cells[row, 2].Value = "Shop";
        StyleSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        WriteHeader(ws, row, new[]
        {
            "Pod - Shops", "Shop ID", "Total Calls", "Overflow", "Queued", "Handle",
            "Missed Calls", "Transferred Calls", "%Overflow", "%Queued", "%Handled",
            "%missed", "%Transferred", "Order Count", "Conv %", "Refunded  Orders",
            "% Orders with errors",
        });
        row++;

        var shopRows = BuildShopRowsForExport(report);
        if (shopRows.Count == 0)
        {
            // Placeholder del template original cuando no hay datos de shop.
            ws.Cells[row, 2].Value = "Capri Pizza Pasta Kabobs";
            ws.Cells[row, 3].Value = "73";
            row++;
            return;
        }

        foreach (var s in shopRows)
        {
            ws.Cells[row, 2].Value  = s.PodLabel;
            ws.Cells[row, 3].Value  = s.ShopId;
            ws.Cells[row, 4].Value  = s.TotalCalls;
            ws.Cells[row, 5].Value  = s.OverflowCalls;
            ws.Cells[row, 6].Value  = s.QueueCalls;
            ws.Cells[row, 7].Value  = s.HandledCalls;
            ws.Cells[row, 8].Value  = s.MissedCalls;
            ws.Cells[row, 9].Value  = s.TransferredCalls;
            ws.Cells[row, 10].Value = s.PctOverflow;
            ws.Cells[row, 11].Value = s.PctQueued;
            ws.Cells[row, 12].Value = s.PctHandled;
            ws.Cells[row, 13].Value = s.PctMissed;
            ws.Cells[row, 14].Value = s.PctTransferred;
            ws.Cells[row, 15].Value = s.OrderCount;
            ws.Cells[row, 16].Value = s.ConversionRate;
            ws.Cells[row, 17].Value = s.RefundedOrders;
            ws.Cells[row, 18].Value = s.ErrorRate;
            row++;
        }
    }

    private static List<DailyGlobalRow> GetPlaceholderGlobalRows()
    {
        var rows = new List<DailyGlobalRow>(DefaultPodPlaceholders.Length);
        foreach (var pod in DefaultPodPlaceholders)
        {
            rows.Add(new DailyGlobalRow { Pod = pod });
        }
        return rows;
    }

    private static List<DailyAgentRow> GetPlaceholderAgentRows()
    {
        var rows = new List<DailyAgentRow>();
        foreach (var pod in DefaultPodPlaceholders)
        {
            // Dos filas placeholder por POD (Full Time / Part Time) para que
            // el sub-bloque tenga la misma forma que el template original.
            rows.Add(new DailyAgentRow { Pod = pod, AgentEmail = "agent.name@slice.com", Shift = "Full Time" });
            rows.Add(new DailyAgentRow { Pod = pod, AgentEmail = "agent.name@slice.com", Shift = "Part Time" });
        }
        return rows;
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

    /// <summary>
    /// Construye las filas de Shop cruzando <c>ShopDaily</c> (que tiene
    /// OrderCount/Conv/Refunded/Error) con <c>ShopCallMetrics</c> (que tiene las
    /// metricas de llamadas por Shop+Pod+Week). Cuando ambos existen para un
    /// mismo shop, se prefiere la fila de ShopCallMetrics con la WeekStart mas
    /// reciente. Si solo hay ShopDaily, igual se emite la fila con las
    /// metricas de llamadas en 0/vacias.
    /// </summary>
    private static List<ShopRowExport> BuildShopRowsForExport(SliceReport report)
    {
        if (report.ShopDaily.Count == 0 && report.ShopCallMetrics.Count == 0)
        {
            return new List<ShopRowExport>();
        }

        var metricsByShop = report.ShopCallMetrics
            .GroupBy(m => m.ShopName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.WeekStart).First(),
                StringComparer.OrdinalIgnoreCase);

        var seenShops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ShopRowExport>();

        foreach (var s in report.ShopDaily)
        {
            seenShops.Add(s.ShopName);
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

        // Shops que solo tienen metricas de llamadas (sin ShopDaily): los
        // incluimos para no perder datos.
        foreach (var kv in metricsByShop)
        {
            if (seenShops.Contains(kv.Key)) continue;
            var m = kv.Value;
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
            });
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
