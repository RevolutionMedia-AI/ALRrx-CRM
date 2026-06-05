using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetAgentPerformanceWithSalesUseCase
{
    private readonly GetReportUseCase _getReport;
    private readonly IVicidialSalesRepository _salesRepo;
    private readonly ILogger<GetAgentPerformanceWithSalesUseCase> _logger;

    public GetAgentPerformanceWithSalesUseCase(
        GetReportUseCase getReport,
        IVicidialSalesRepository salesRepo,
        ILogger<GetAgentPerformanceWithSalesUseCase> logger)
    {
        _getReport = getReport;
        _salesRepo = salesRepo;
        _logger = logger;
    }

    public async Task<ReportDto> ExecuteAsync(TimeFilterDto filter, CancellationToken ct = default)
    {
        var baseReport = await _getReport.ExecuteAsync("agent_performance", filter, ct);

        var (from, to) = ExtractRange(filter);
        Dictionary<string, FormSalesByAgentRow> formSales;
        try
        {
            formSales = await _salesRepo.GetFormSalesByAgentAsync(from, to, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load form sales by agent — returning base report without enrichment");
            formSales = new Dictionary<string, FormSalesByAgentRow>();
        }

        var enrichedRows = new Dictionary<string, object?>[baseReport.Rows.Length];
        var newColumns = baseReport.Columns.Concat(new[] { "Form_Sales_Count", "Form_Sales_Amount" }).ToArray();

        for (var i = 0; i < baseReport.Rows.Length; i++)
        {
            var row = new Dictionary<string, object?>(baseReport.Rows[i]);
            var name = row.TryGetValue("Name", out var n) ? n?.ToString() ?? string.Empty : string.Empty;
            if (formSales.TryGetValue(name, out var sales))
            {
                row["Form_Sales_Count"] = sales.Count;
                row["Form_Sales_Amount"] = sales.Amount;
            }
            else
            {
                row["Form_Sales_Count"] = 0;
                row["Form_Sales_Amount"] = 0m;
            }
            enrichedRows[i] = row;
        }

        return new ReportDto
        {
            ReportName = baseReport.ReportName + " + Form Sales",
            Columns = newColumns,
            Rows = enrichedRows,
            GeneratedAt = baseReport.GeneratedAt,
            TimeRangeStart = baseReport.TimeRangeStart,
            TimeRangeEnd = baseReport.TimeRangeEnd,
        };
    }

    private static (string? from, string? to) ExtractRange(TimeFilterDto filter)
    {
        if (filter.CustomStart.HasValue && filter.CustomEnd.HasValue)
        {
            return (
                filter.CustomStart.Value.ToString("yyyy-MM-dd HH:mm:ss"),
                filter.CustomEnd.Value.ToString("yyyy-MM-dd HH:mm:ss")
            );
        }
        var tr = TimeFilterHelper.BuildTimeRange(filter);
        return (tr.Start.ToString("yyyy-MM-dd HH:mm:ss"), tr.End.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
