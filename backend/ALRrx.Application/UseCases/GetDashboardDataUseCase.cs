using ALRrx.Application.DTOs;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Application.UseCases;

public sealed class GetDashboardDataUseCase
{
    private readonly IQueryService _queryService;

    public GetDashboardDataUseCase(IQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<DashboardSummaryDto> ExecuteAsync(TimeFilterDto filter, CancellationToken ct = default)
    {
        var timeRange = BuildTimeRange(filter);

        var salesTask = _queryService.ExecuteQueryAsync("ventas_hoy", timeRange, ct);
        var contactTask = _queryService.ExecuteQueryAsync("contact_vs_nocontact", timeRange, ct);
        var dispositionsTask = _queryService.ExecuteQueryAsync("dispositions", timeRange, ct);
        var agentTask = _queryService.ExecuteQueryAsync("agent_performance", timeRange, ct);

        await Task.WhenAll(salesTask, contactTask, agentTask, dispositionsTask);

        var salesResult = salesTask.Result;
        var contactResult = contactTask.Result;
        var dispositionsResult = dispositionsTask.Result;
        var agentResult = agentTask.Result;

        var metrics = new List<MetricCardDto>();

        if (salesResult.Rows.Length > 0)
        {
            metrics.Add(new MetricCardDto
            {
                Label = "Sales Today",
                Value = salesResult.Rows[0].GetValueOrDefault("Sales_Today")?.ToString() ?? "0",
                Color = "#10b981",
                Format = "number"
            });
        }

        if (contactResult.Rows.Length > 0)
        {
            var row = contactResult.Rows[0];
            metrics.Add(new MetricCardDto
            {
                Label = "Contacts",
                Value = row.GetValueOrDefault("Contact")?.ToString() ?? "0",
                Color = "#4f46e5",
                Format = "number"
            });
            metrics.Add(new MetricCardDto
            {
                Label = "No Contacts",
                Value = row.GetValueOrDefault("No_Contact")?.ToString() ?? "0",
                Color = "#ef4444",
                Format = "number"
            });
            metrics.Add(new MetricCardDto
            {
                Label = "Total Calls",
                Value = row.GetValueOrDefault("Total_Calls")?.ToString() ?? "0",
                Color = "#f59e0b",
                Format = "number"
            });
        }

        var charts = new List<ChartDataDto>();

        if (dispositionsResult.Rows.Length > 0)
        {
            charts.Add(BuildChartFromReport(dispositionsResult, "pie", "Dispositions"));
        }

        if (agentResult.Rows.Length > 0)
        {
            charts.Add(BuildChartFromReport(agentResult, "bar", "Agent Performance (Sales)"));
        }

        return new DashboardSummaryDto
        {
            Metrics = metrics,
            Charts = charts,
            LastUpdated = DateTime.UtcNow
        };
    }

    private static ChartDataDto BuildChartFromReport(Domain.Entities.ReportResult report, string chartType, string title)
    {
        if (report.Rows.Length == 0 || report.Columns.Length < 2)
            return new ChartDataDto { ChartType = chartType, Title = title };

        var labelColumn = report.Columns[0];
        var valueColumn = report.Columns[1];

        var labels = report.Rows.Select(r => r.GetValueOrDefault(labelColumn)?.ToString() ?? "").ToArray();
        var data = report.Rows.Select(r =>
        {
            var val = r.GetValueOrDefault(valueColumn);
            return Convert.ToDecimal(val ?? 0);
        }).ToArray();

        return new ChartDataDto
        {
            ChartType = chartType,
            Title = title,
            Labels = labels,
            Series = [new ChartSeriesDto { Name = valueColumn, Data = data }]
        };
    }

    private static TimeRange BuildTimeRange(TimeFilterDto filter)
    {
        if (Enum.TryParse<Domain.Enums.TimePeriod>(filter.Period, out var period))
            return period == Domain.Enums.TimePeriod.Custom
                ? TimeRange.FromCustom(filter.CustomStart!.Value, filter.CustomEnd!.Value)
                : TimeRange.FromPeriod(period);

        return TimeRange.FromPeriod(Domain.Enums.TimePeriod.Today);
    }
}
