using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Domain.Interfaces;

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
        var timeRange = TimeFilterHelper.BuildTimeRange(filter);

        var salesTask = _queryService.ExecuteQueryAsync("ventas_hoy", timeRange, ct);
        var contactTask = _queryService.ExecuteQueryAsync("contact_vs_nocontact", timeRange, ct);
        var dispositionsTask = _queryService.ExecuteQueryAsync("dispositions", timeRange, ct);
        var agentTask = _queryService.ExecuteQueryAsync("agent_performance", timeRange, ct);
        var ahtTask = _queryService.ExecuteQueryAsync("aht_daily", timeRange, ct);
        var occupancyTask = _queryService.ExecuteQueryAsync("occupancy_rate", timeRange, ct);
        var leadsTask = _queryService.ExecuteQueryAsync("leads_contact_rate", timeRange, ct);

        await Task.WhenAll(salesTask, contactTask, agentTask, dispositionsTask, ahtTask, occupancyTask, leadsTask);

        var salesResult = salesTask.Result;
        var contactResult = contactTask.Result;
        var dispositionsResult = dispositionsTask.Result;
        var agentResult = agentTask.Result;
        var ahtResult = ahtTask.Result;
        var occupancyResult = occupancyTask.Result;
        var leadsResult = leadsTask.Result;

        var metrics = new List<MetricCardDto>();

        if (leadsResult.Rows.Length > 0)
        {
            var lr = leadsResult.Rows[0];
            metrics.Add(new MetricCardDto
            {
                Label = "Leads Dialed",
                Value = lr.GetValueOrDefault("Total_Dialed_Leads")?.ToString() ?? "0",
                Color = "#3B82F6",
                Format = "number"
            });
            metrics.Add(new MetricCardDto
            {
                Label = "Leads Contacted",
                Value = lr.GetValueOrDefault("Contacted_Leads")?.ToString() ?? "0",
                Color = "#10b981",
                Format = "number"
            });
            metrics.Add(new MetricCardDto
            {
                Label = "Contact Rate",
                Value = $"{lr.GetValueOrDefault("Contact_Rate") ?? 0}%",
                Color = "#8B5CF6",
                Format = "percentage"
            });
        }

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

            var salesVal = salesResult.Rows.Length > 0
                ? Convert.ToInt32(salesResult.Rows[0].GetValueOrDefault("Sales_Today") ?? 0)
                : 0;
            var contactsVal = Convert.ToInt32(row.GetValueOrDefault("Contact") ?? 0);
            var overallConvPct = contactsVal > 0 ? Math.Round((double)salesVal / contactsVal * 100, 1) : 0;
            metrics.Add(new MetricCardDto
            {
                Label = "Overall Conversion",
                Value = $"{overallConvPct:0.0}%",
                Color = overallConvPct >= 10 ? "#10b981" : overallConvPct >= 5 ? "#f59e0b" : "#ef4444",
                Format = "percentage"
            });
        }

        if (ahtResult.Rows.Length > 0)
        {
            var ahtVal = ahtResult.Rows[0].GetValueOrDefault("AHT_Minutes");
            metrics.Add(new MetricCardDto
            {
                Label = "Avg Handle Time",
                Value = $"{ahtVal ?? 0} min",
                Color = "#3B82F6",
                Format = "duration"
            });
        }

        if (occupancyResult.Rows.Length > 0)
        {
            var occVal = occupancyResult.Rows[0].GetValueOrDefault("Occupancy_Pct");
            metrics.Add(new MetricCardDto
            {
                Label = "Occupancy",
                Value = $"{occVal ?? 0}%",
                Color = "#8B5CF6",
                Format = "percentage"
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

    private static bool IsNumericValue(object? val) => val switch
    {
        short or int or long or ushort or uint or ulong or float or double or decimal => true,
        string s => decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _),
        _ => false,
    };

    private static ChartDataDto BuildChartFromReport(Domain.Entities.ReportResult report, string chartType, string title)
    {
        if (report.Rows.Length == 0 || report.Columns.Length < 2)
            return new ChartDataDto { ChartType = chartType, Title = title };

        var labelColumn = report.Columns[0];
        var valueColumn = report.Columns.Skip(1)
            .FirstOrDefault(c => report.Rows.Any(r => IsNumericValue(r.GetValueOrDefault(c))))
            ?? report.Columns[^1];

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

    }
