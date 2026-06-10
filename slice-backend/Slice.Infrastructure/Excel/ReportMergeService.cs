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

    /// <summary>
    /// Resolves the directory used to write the merged XLSX/CSV exports.
    /// Prefers <c>/data/slice/exports</c> so the files survive container restarts
    /// (Northflank mounts a persistent volume there). Falls back to <c>$TMPDIR/slice/exports</c>
    /// when the persistent volume is not writable (e.g. local dev on Windows / macOS).
    /// </summary>
    private string ResolveExportDir()
    {
        var persistent = "/data/slice/exports";
        try
        {
            Directory.CreateDirectory(persistent);
            // Probe write access with a throwaway file — creating the dir alone
            // is not enough; a read-only mount would still let CreateDirectory succeed.
            var probe = Path.Combine(persistent, $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return persistent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Persistent export dir {Dir} is not writable; falling back to {Tmp}.",
                persistent, Path.GetTempPath());
            var fallback = Path.Combine(Path.GetTempPath(), "slice", "exports");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

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
                    // Tomamos el primer ShopId no vacio del grupo (todos deberian
                    // ser iguales para una misma ShopName).
                    var shopId = e.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.ShopId))?.ShopId ?? string.Empty;
                    return new ShopDailyRow
                    {
                        ShopName       = grp.Key,
                        ShopId         = shopId,
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

        // The four order metrics in DailyGlobal (OrderCount, RefundedOrders,
        // PctOrdersWithErrors, ConvPct) often come from a separate CSV that
        // the parser may not have ingested. Backfill them from ShopDaily via
        // ShopCallMetrics (which maps Pod -> ShopId) so the persisted report
        // shows the real numbers, not zeros.
        BackfillOrderMetricsFromShopDaily(merged);

        return merged;
    }

    /// <inheritdoc/>
    public async Task<string> ExportXlsxAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir   = ResolveExportDir();
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
        _logger.LogInformation("XLSX exported to {Path} ({Bytes} bytes)", filePath, new FileInfo(filePath).Length);
        return filePath;
    }

    /// <inheritdoc/>
    public async Task<string> ExportCsvAsync(SliceReport report, CancellationToken ct = default)
    {
        var outDir   = ResolveExportDir();
        var filePath = Path.Combine(outDir, $"Slice_Report_{report.Id}.csv");

        var sb = new StringBuilder();

        // ── Bloque Global ─────────────────────────────────────────────────────
        sb.AppendLine("=== Global ===");
        sb.AppendLine("Pod,Queued,Handle,Missed Calls,Transferred Calls,%Queued,%Handled,%missed,%Transferred,Conv %,Order Count,Refunded Orders,% Orders with errors");
        var globalRows = ResolveGlobalRowsForExport(report);
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
        // ── Bloque Shop: dos sub-tablas (Call Metrics + Orders) ──────────────
        sb.AppendLine("=== Shop Call Metrics (by shop, pod, week) ===");
        sb.AppendLine("Pod,Shop ID,Shop Name,Total Calls,Overflow,Queued,Handle,Missed Calls,Transferred Calls,%Overflow,%Queued,%Handled,%missed,%Transferred");
        var callRows = BuildShopCallMetricsRowsForExport(report);
        foreach (var s in callRows)
            sb.AppendLine($"{s.PodLabel},{s.ShopId},{s.ShopName},{s.TotalCalls},{s.OverflowCalls},{s.QueueCalls},{s.HandledCalls},{s.MissedCalls},{s.TransferredCalls}," +
                          $"{s.PctOverflow:F2},{s.PctQueued:F2},{s.PctHandled:F2},{s.PctMissed:F2},{s.PctTransferred:F2}");

        sb.AppendLine();
        sb.AppendLine("=== Shop Orders (by shop) ===");
        sb.AppendLine("Shop ID,Shop Name,Total Orders,Refunded Orders,% Orders with errors,Conv %");
        var orderRows = BuildShopOrdersRowsForExport(report);
        foreach (var s in orderRows)
            sb.AppendLine($"{s.ShopId},{s.ShopName},{s.TotalOrders},{s.RefundedOrders},{s.ErrorRate:F2},{s.ConversionRate:F2}");

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

        var globalRows = ResolveGlobalRowsForExport(report);

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
    /// Resuelve las filas del bloque Global. Orden de prioridad:
    /// 1. <c>report.DailyGlobal</c> si tiene al menos una fila con datos
    ///    reales (volumenes distintos de 0). Despues del rewrite del parser
    ///    TryParsePodLevelPivoted, DailyGlobal ya viene bien parseado
    ///    directamente del CSV (volumenes correctos, % correctos, dia/semana
    ///    mas reciente, no suma acumulada).
    /// 2. Si DailyGlobal esta vacio, agregar ShopCallMetrics por PodId
    ///    (semanal, datos de la semana mas reciente por shop) + cruzar con
    ///    ShopDaily para los OrderCount/RefundedOrders/%OrdersWithErrors.
    /// 3. Placeholders con los PODs tipicos (ES-12/16/17/18).
    /// </summary>
    private static List<DailyGlobalRow> ResolveGlobalRowsForExport(SliceReport report)
    {
        var dailyWithData = report.DailyGlobal
            .Where(r => r.Queued + r.Handled + r.MissedCalls + r.TransferredCalls +
                        r.OrderCount + r.RefundedOrders > 0)
            .ToList();
        if (dailyWithData.Count > 0)
        {
            // The Global block has real call-volume data per pod. But the four
            // order metrics (OrderCount, RefundedOrders, PctOrdersWithErrors,
            // ConvPct) are often missing in the source — they live in a
            // different CSV that the parser may or may not have ingested.
            // Backfill from ShopDaily via ShopCallMetrics (the bridge that
            // maps Pod -> ShopId) so the export shows real numbers per pod.
            BackfillOrderMetricsFromShopDaily(report);
            return report.DailyGlobal.ToList();
        }

        if (report.ShopCallMetrics.Count > 0)
        {
            return AggregateShopMetricsByPod(report);
        }

        return GetPlaceholderGlobalRows();
    }

    /// <summary>
    /// For each DailyGlobalRow that has 0s in the four order metrics, derive
    /// OrderCount, RefundedOrders and PctOrdersWithErrors from ShopDaily rows
    /// belonging to the same pod. We use ShopCallMetrics as the bridge Pod -> ShopName
    /// (and fall back to ShopId, but ShopName is more reliable across the
    /// shop_level_-_call_metrics.csv and shop_level_-_orders_metrics.csv files,
    /// which use different ShopId schemes for the same shop).
    /// </summary>
    private static void BackfillOrderMetricsFromShopDaily(SliceReport report)
    {
        if (report.ShopCallMetrics.Count == 0 || report.ShopDaily.Count == 0) return;

        // Build PodId -> set of shop identifiers (ShopName preferred, ShopId fallback).
        // The user's CSVs use different ShopId schemes between call metrics and orders
        // metrics, so ShopName is the only reliable cross-reference.
        var shopsByPod = report.ShopCallMetrics
            .Where(m => !string.IsNullOrWhiteSpace(m.PodId))
            .GroupBy(m => m.PodId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var names = g.Select(m => m.ShopName)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => NormalizeShopName(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (names.Count > 0) return names;
                    return g.Select(m => m.ShopId)
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Select(id => NormalizeShopName(id))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                },
                StringComparer.OrdinalIgnoreCase);

        // Build normalized shop name -> ShopDaily row (first match wins).
        var dailyByShop = report.ShopDaily
            .GroupBy(s => NormalizeShopName(string.IsNullOrWhiteSpace(s.ShopName) ? s.ShopId : s.ShopName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var g in report.DailyGlobal)
        {
            if (g.OrderCount + g.RefundedOrders > 0) continue; // already populated
            if (!shopsByPod.TryGetValue(g.Pod, out var shopKeys)) continue;

            int orders = 0;
            int refunded = 0;
            double errSum = 0;
            int errCount = 0;
            int matched = 0;
            foreach (var key in shopKeys)
            {
                if (!dailyByShop.TryGetValue(key, out var s)) continue;
                orders   += s.TotalOrders;
                refunded += s.RefundedOrders;
                if (s.ErrorRate > 0)
                {
                    errSum  += s.ErrorRate;
                    errCount += 1;
                }
                matched++;
            }
            if (orders + refunded > 0)
            {
                g.OrderCount          = orders;
                g.RefundedOrders      = refunded;
                g.PctOrdersWithErrors = errCount > 0 ? errSum / errCount : 0;
            }
        }
    }

    /// <summary>
    /// Strips whitespace, punctuation and common business suffixes (LLC, Inc, Pizzeria, Pizza, etc.)
    /// so two shop labels that differ only in suffix/case/spacing can be matched.
    /// </summary>
    private static string NormalizeShopName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.Trim().ToLowerInvariant();
        // Drop apostrophes and punctuation.
        var cleaned = new string(lower.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
        // Collapse whitespace.
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
        // Drop common trailing suffixes so "Papa Ray's Pizza" matches "Papa Ray's".
        foreach (var suffix in new[] { " pizza", " pizzeria", " llc", " inc", " co", " restaurant", " kitchen" })
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^suffix.Length].TrimEnd();
        }
        return cleaned;
    }

    /// <summary>
    /// Agrega las metricas de llamadas de <c>ShopCallMetrics</c> agrupadas por
    /// <c>PodId</c>, tomando la WeekStart mas reciente por (ShopId, PodId).
    /// Los volumenes se suman, los porcentajes se recalculan a partir de
    /// los volumenes agregados.
    /// Ademas, cruza con <c>ShopDaily</c> por <c>ShopId</c> para obtener
    /// OrderCount, RefundedOrders y PctOrdersWithErrors por POD (sumando
    /// las metricas de orders de cada tienda que pertenece al POD).
    /// </summary>
    private static List<DailyGlobalRow> AggregateShopMetricsByPod(SliceReport report)
    {
        var metrics = report.ShopCallMetrics;

        // Mapeo ShopId -> PodId usando la WeekStart mas reciente de cada shop.
        var shopToPod = metrics
            .Where(m => !string.IsNullOrWhiteSpace(m.ShopId) && !string.IsNullOrWhiteSpace(m.PodId))
            .GroupBy(m => m.ShopId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(m => m.WeekStart).First().PodId,
                StringComparer.OrdinalIgnoreCase);

        // Mapeo ShopId -> datos de orders (del parser de shop_level_-_orders_metrics).
        var ordersByShop = report.ShopDaily
            .Where(s => !string.IsNullOrWhiteSpace(s.ShopId))
            .GroupBy(s => s.ShopId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        // Pre-agregar orders por PodId para no recalcular en cada grupo.
        var ordersByPod = new Dictionary<string, (int Orders, int Refunded, double ErrSum, int ErrCount)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ordersByShop)
        {
            if (!shopToPod.TryGetValue(kv.Key, out var pod)) continue;
            var s = kv.Value;
            if (!ordersByPod.TryGetValue(pod, out var agg))
            {
                agg = (0, 0, 0, 0);
            }
            agg.Orders   += s.TotalOrders;
            agg.Refunded += s.RefundedOrders;
            if (s.ErrorRate > 0)
            {
                agg.ErrSum  += s.ErrorRate;
                agg.ErrCount += 1;
            }
            ordersByPod[pod] = agg;
        }

        var result = new List<DailyGlobalRow>();

        foreach (var podGroup in metrics
            .GroupBy(m => string.IsNullOrWhiteSpace(m.PodId) ? "(unassigned)" : m.PodId))
        {
            var rows = podGroup.ToList();
            if (rows.Count == 0) continue;

            int totalCalls   = rows.Sum(r => r.TotalCalls);
            int overflow     = rows.Sum(r => r.OverflowCalls);
            int queued       = rows.Sum(r => r.QueueCalls);
            int handled      = rows.Sum(r => r.HandledCalls);
            int missed       = rows.Sum(r => r.MissedCalls);
            int transferred  = rows.Sum(r => r.TransferredCalls);

            ordersByPod.TryGetValue(podGroup.Key, out var orders);

            result.Add(new DailyGlobalRow
            {
                Pod                = podGroup.Key,
                Queued             = queued,
                Handled            = handled,
                MissedCalls        = missed,
                TransferredCalls   = transferred,
                PctQueued          = totalCalls > 0 ? (double)queued      / totalCalls * 100 : 0,
                PctHandled         = totalCalls > 0 ? (double)handled     / totalCalls * 100 : 0,
                PctMissed          = queued    > 0 ? (double)missed      / queued   * 100 : 0,
                PctTransferred     = totalCalls > 0 ? (double)transferred / totalCalls * 100 : 0,
                ConvPct            = 0,
                OrderCount         = orders.Orders,
                RefundedOrders     = orders.Refunded,
                PctOrdersWithErrors= orders.ErrCount > 0 ? orders.ErrSum / orders.ErrCount : 0,
            });
        }

        return result
            .OrderBy(r => r.Pod, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    /// <summary>
    /// Bloque Shop. El Excel/CSV del usuario y los CSVs del ZIP usan esquemas
    /// de ShopId distintos entre call metrics y orders metrics, asi que en vez
    /// de cruzar por ID (que dejaria la mayoria de los orders en 0), emitimos
    /// dos sub-tablas: una de Shop Call Metrics (volumenes de llamadas por
    /// shop/semana/POD) y otra de Shop Orders (orders, refunds, %errors, conv%
    /// por shop). Las dos tablas son independientes; el backfill del Global
    /// usa ShopName como puente entre las dos.
    /// </summary>
    private static void WriteShopBlock(ExcelWorksheet ws, ref int row, SliceReport report)
    {
        ws.Cells[row, 2].Value = "Shop";
        StyleSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        // ── Sub-tabla 1: Shop Call Metrics ─────────────────────────────────
        ws.Cells[row, 2].Value = "Shop Call Metrics (by shop, pod, week)";
        StyleSubSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        WriteHeader(ws.Cells[row, 2, row, 18], new[]
        {
            "Pod", "Shop ID", "Shop Name", "Total Calls", "Overflow", "Queued",
            "Handle", "Missed Calls", "Transferred Calls", "%Overflow", "%Queued",
            "%Handled", "%missed", "%Transferred", "Order Count", "Conv %",
            "Refunded  Orders", "% Orders with errors",
        });
        row++;

        var callRows = BuildShopCallMetricsRowsForExport(report);
        if (callRows.Count == 0)
        {
            ws.Cells[row, 2].Value = "—";
            ws.Cells[row, 3].Value = "No shop call metrics in this report.";
            row++;
        }
        else
        {
            foreach (var s in callRows)
            {
                ws.Cells[row, 2].Value  = s.PodLabel;
                ws.Cells[row, 3].Value  = s.ShopId;
                ws.Cells[row, 4].Value  = s.ShopName;
                ws.Cells[row, 5].Value  = s.TotalCalls;
                ws.Cells[row, 6].Value  = s.OverflowCalls;
                ws.Cells[row, 7].Value  = s.QueueCalls;
                ws.Cells[row, 8].Value  = s.HandledCalls;
                ws.Cells[row, 9].Value  = s.MissedCalls;
                ws.Cells[row, 10].Value = s.TransferredCalls;
                ws.Cells[row, 11].Value = s.PctOverflow;
                ws.Cells[row, 12].Value = s.PctQueued;
                ws.Cells[row, 13].Value = s.PctHandled;
                ws.Cells[row, 14].Value = s.PctMissed;
                ws.Cells[row, 15].Value = s.PctTransferred;
                row++;
            }
        }

        row++; // spacer

        // ── Sub-tabla 2: Shop Orders ────────────────────────────────────────
        ws.Cells[row, 2].Value = "Shop Orders (by shop)";
        StyleSubSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        WriteHeader(ws.Cells[row, 2, row, 6], new[]
        {
            "Shop ID", "Shop Name", "Total Orders", "Refunded Orders",
            "% Orders with errors", "Conv %",
        });
        row++;

        var orderRows = BuildShopOrdersRowsForExport(report);
        if (orderRows.Count == 0)
        {
            ws.Cells[row, 2].Value = "—";
            ws.Cells[row, 3].Value = "No shop orders data in this report.";
            row++;
        }
        else
        {
            foreach (var s in orderRows)
            {
                ws.Cells[row, 2].Value = s.ShopId;
                ws.Cells[row, 3].Value = s.ShopName;
                ws.Cells[row, 4].Value = s.TotalOrders;
                ws.Cells[row, 5].Value = s.RefundedOrders;
                ws.Cells[row, 6].Value = s.ErrorRate;
                ws.Cells[row, 7].Value = s.ConversionRate;
                row++;
            }
        }
    }

    private static List<ShopRowExport> BuildShopCallMetricsRowsForExport(SliceReport report)
    {
        // Keep only the most recent (ShopId, PodId, ShopName) triple per group.
        var result = new List<ShopRowExport>();
        foreach (var grp in report.ShopCallMetrics
            .GroupBy(m => (m.ShopId, m.PodId, m.ShopName), comparer: ShopPodNameKeyComparer.Instance)
            .OrderBy(g => g.Key.PodId).ThenBy(g => g.Key.ShopId))
        {
            var m = grp.OrderByDescending(x => x.WeekStart).First();
            result.Add(new ShopRowExport
            {
                PodLabel        = string.IsNullOrWhiteSpace(m.PodId) ? "Pod" : m.PodId,
                ShopId          = m.ShopId,
                ShopName        = m.ShopName,
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

    private static List<ShopOrdersRowExport> BuildShopOrdersRowsForExport(SliceReport report)
    {
        var result = new List<ShopOrdersRowExport>();
        foreach (var grp in report.ShopDaily
            .GroupBy(s => (s.ShopId, s.ShopName), comparer: ShopNameKeyComparer.Instance)
            .OrderBy(g => g.Key.ShopName).ThenBy(g => g.Key.ShopId))
        {
            var s = grp.First();
            if (s.TotalOrders + s.RefundedOrders + s.ErrorRate + s.ConversionRate == 0) continue;
            result.Add(new ShopOrdersRowExport
            {
                ShopId         = s.ShopId,
                ShopName       = s.ShopName,
                TotalOrders    = s.TotalOrders,
                RefundedOrders = s.RefundedOrders,
                ErrorRate      = s.ErrorRate,
                ConversionRate = s.ConversionRate,
            });
        }
        return result;
    }

    private readonly struct ShopOrdersRowExport
    {
        public string ShopId         { get; init; }
        public string ShopName       { get; init; }
        public int    TotalOrders    { get; init; }
        public int    RefundedOrders { get; init; }
        public double ErrorRate      { get; init; }
        public double ConversionRate { get; init; }
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
        public string ShopName        { get; init; }
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
    }

    /// <summary>
    /// Construye las filas de Shop para el snapshot. Reemplazado por
    /// <c>BuildShopCallMetricsRowsForExport</c> + <c>BuildShopOrdersRowsForExport</c>
    /// porque el join por ShopId entre ShopCallMetrics y ShopDaily no funciona
    /// (los dos CSVs usan esquemas de ShopId distintos). Esta version se deja
    /// comentada por si se necesita en el futuro con un mapeo explicito de IDs.
    /// </summary>
    /*
    private static List<ShopRowExport> BuildShopRowsForExport(SliceReport report)
    {
        // ... (codigo eliminado en bust-16, ver commit para el detalle)
    }
    */

    /// <summary>Comparer case-insensible para tuplas (ShopId, PodId, ShopName) usado en el GroupBy de ShopCallMetrics para el export.</summary>
    private sealed class ShopPodNameKeyComparer : IEqualityComparer<(string ShopId, string PodId, string ShopName)>
    {
        public static readonly ShopPodNameKeyComparer Instance = new();
        public bool Equals((string ShopId, string PodId, string ShopName) a, (string ShopId, string PodId, string ShopName) b) =>
            string.Equals(a.ShopId, b.ShopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.PodId, b.PodId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ShopName, b.ShopName, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string ShopId, string PodId, string ShopName) obj) =>
            HashCode.Combine(
                obj.ShopId?.ToLowerInvariant(),
                obj.PodId?.ToLowerInvariant(),
                obj.ShopName?.ToLowerInvariant());
    }

    /// <summary>Comparer case-insensible para tuplas (ShopId, ShopName) usado en el GroupBy de ShopDaily.</summary>
    private sealed class ShopNameKeyComparer : IEqualityComparer<(string ShopId, string ShopName)>
    {
        public static readonly ShopNameKeyComparer Instance = new();
        public bool Equals((string ShopId, string ShopName) a, (string ShopId, string ShopName) b) =>
            string.Equals(a.ShopId, b.ShopId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.ShopName, b.ShopName, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string ShopId, string ShopName) obj) =>
            HashCode.Combine(
                obj.ShopId?.ToLowerInvariant(),
                obj.ShopName?.ToLowerInvariant());
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

    /// <summary>Estilo para los titulos de sub-seccion dentro de un bloque (e.g. 'Shop Call Metrics' dentro de 'Shop').</summary>
    private static void StyleSubSectionTitle(ExcelRange range)
    {
        range.Merge = true;
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 11;
        range.Style.Font.Italic = true;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Left;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(248, 248, 248));
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
