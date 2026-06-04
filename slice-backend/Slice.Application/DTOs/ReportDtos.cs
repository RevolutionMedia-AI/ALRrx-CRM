using Slice.Domain.Entities;

namespace Slice.Application.DTOs;

/// <summary>Resumen de un reporte para listados (sin datos de filas).</summary>
public record ReportSummaryDto(
    string Id,
    DateTime ReportDate,
    DateTime GeneratedAt,
    int PodCount,
    int AgentCount,
    string? MergedCsvPath,
    string? MergedXlsxPath);

/// <summary>Datos formateados para un gráfico de líneas o barras.</summary>
public record ChartDataDto(
    string Label,
    IReadOnlyList<ChartSeriesDto> Series);

/// <summary>Una serie de valores dentro de un gráfico.</summary>
public record ChartSeriesDto(string Name, IReadOnlyList<double> Values);

/// <summary>Cuerpo de la solicitud para enviar un reporte por email.</summary>
public record SendReportEmailRequest(string ToEmail, string ReportId, string Subject);

// ─── Patch DTOs (used by PATCH endpoints, all fields optional) ───────────────

/// <summary>Campos opcionales para editar una fila del Daily Global.</summary>
public record DailyGlobalRowPatch(
    int? Queued, int? Handled, int? MissedCalls, int? TransferredCalls,
    double? PctQueued, double? PctHandled, double? PctMissed, double? PctTransferred,
    double? ConvPct, int? OrderCount, int? RefundedOrders, double? PctOrdersWithErrors);

/// <summary>Campos opcionales para editar una fila del Daily Agent.</summary>
public record DailyAgentRowPatch(
    int? HC, int? TC, int? NumberOfHolds,
    double? AvgHoldTime, double? ASA, double? AHT, double? ACW,
    double? PctContactsOnHold, double? PctSLUnder15Sec, double? PctTransfers,
    string? Shift, string? SupervisorName);

/// <summary>Campos opcionales para editar una fila del Shop Daily.</summary>
public record ShopDailyRowPatch(
    int? TotalOrders, int? RefundedOrders, double? ErrorRate, double? ConversionRate);

// ─── Mapper ──────────────────────────────────────────────────────────────────

/// <summary>Convierte entidades <see cref="SliceReport"/> a DTOs para la API.</summary>
public static class ReportDtoMapper
{
    /// <summary>Proyecta un reporte a su resumen sin datos de filas.</summary>
    public static ReportSummaryDto ToSummary(SliceReport r) => new(
        r.Id,
        r.ReportDate,
        r.GeneratedAt,
        r.DailyGlobal.Select(g => g.Pod).Distinct().Count(),
        r.DailyAgents.Count,
        r.MergedCsvPath,
        r.MergedXlsxPath);

    /// <summary>Proyecta el Daily Global a series de datos para gráficas.</summary>
    public static ChartDataDto ToGlobalChart(SliceReport r)
    {
        return new ChartDataDto(
            "Daily Global",
            [
                new ChartSeriesDto("Queued",      r.DailyGlobal.Select(g => (double)g.Queued).ToList()),
                new ChartSeriesDto("Handled",     r.DailyGlobal.Select(g => (double)g.Handled).ToList()),
                new ChartSeriesDto("Missed",      r.DailyGlobal.Select(g => (double)g.MissedCalls).ToList()),
                new ChartSeriesDto("Transferred", r.DailyGlobal.Select(g => (double)g.TransferredCalls).ToList()),
                new ChartSeriesDto("Conv %",      r.DailyGlobal.Select(g => g.ConvPct).ToList()),
                new ChartSeriesDto("Orders",      r.DailyGlobal.Select(g => (double)g.OrderCount).ToList()),
            ]);
    }
}
