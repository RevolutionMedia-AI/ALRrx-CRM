using Slice.Domain.Entities;

namespace Slice.Application.DTOs;

public record ReportSummaryDto(
    string Id,
    DateTime ReportDate,
    DateTime GeneratedAt,
    int PodCount,
    int AgentCount,
    string? MergedCsvPath,
    string? MergedXlsxPath);

public record ChartDataDto(
    string Label,
    IReadOnlyList<ChartSeriesDto> Series);

public record ChartSeriesDto(string Name, IReadOnlyList<double> Values);

public record SendReportEmailRequest(string ToEmail, string ReportId, string Subject);

public static class ReportDtoMapper
{
    public static ReportSummaryDto ToSummary(SliceReport r) => new(
        r.Id,
        r.ReportDate,
        r.GeneratedAt,
        r.DailyGlobal.Select(g => g.Pod).Distinct().Count(),
        r.DailyAgents.Count,
        r.MergedCsvPath,
        r.MergedXlsxPath);

    public static ChartDataDto ToGlobalChart(SliceReport r)
    {
        var pods = r.DailyGlobal.Select(g => g.Pod).ToList();
        return new ChartDataDto(
            "Daily Global",
            [
                new ChartSeriesDto("Queued", r.DailyGlobal.Select(g => (double)g.Queued).ToList()),
                new ChartSeriesDto("Handled", r.DailyGlobal.Select(g => (double)g.Handled).ToList()),
                new ChartSeriesDto("Missed", r.DailyGlobal.Select(g => (double)g.MissedCalls).ToList()),
                new ChartSeriesDto("Transferred", r.DailyGlobal.Select(g => (double)g.TransferredCalls).ToList()),
                new ChartSeriesDto("Conv %", r.DailyGlobal.Select(g => g.ConvPct).ToList()),
                new ChartSeriesDto("Orders", r.DailyGlobal.Select(g => (double)g.OrderCount).ToList()),
            ]);
    }
}
