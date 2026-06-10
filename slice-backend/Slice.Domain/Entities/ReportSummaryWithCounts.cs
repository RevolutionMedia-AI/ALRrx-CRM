using Slice.Domain.Entities;

namespace Slice.Domain.Entities;

/// <summary>
/// Resumen "summary" de un reporte para los endpoints de lista y periodo.
/// Contiene solo header + counts (NO filas) + opcionalmente el array
/// <c>DailyGlobal</c> cuando lo necesita el endpoint de periodo para el
/// breakdown por pod. Reemplaza al <c>Include × 4</c> que causaba timeouts
/// de 15s en el frontend (bust-18).
/// </summary>
public record ReportSummaryWithCounts(
    string Id,
    DateTime ReportDate,
    DateTime GeneratedAt,
    string GeneratedByEmail,
    /// <summary>Distinct pod names from <c>DailyGlobal</c>.</summary>
    int PodCount,
    /// <summary>Total agent rows.</summary>
    int AgentCount,
    int ShopDailyCount,
    int ShopCallMetricsCount,
    /// <summary>DailyGlobal rows — populated only by period queries that need the per-pod breakdown.</summary>
    IReadOnlyList<DailyGlobalRow> DailyGlobal);
