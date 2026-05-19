using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Application.UseCases;

public sealed class ExportReportUseCase
{
    private readonly IQueryService _queryService;
    private readonly IEnumerable<IReportExportService> _exportServices;

    public ExportReportUseCase(IQueryService queryService, IEnumerable<IReportExportService> exportServices)
    {
        _queryService = queryService;
        _exportServices = exportServices;
    }

    public async Task<ExportResult> ExecuteAsync(ExportRequestDto request, CancellationToken ct = default)
    {
        var timeRange = BuildTimeRange(request.TimeFilter);
        var result = await _queryService.ExecuteQueryAsync(request.ReportId, timeRange, ct);

        var exportService = _exportServices.FirstOrDefault(s =>
            s.Format.Equals(request.Format, StringComparison.OrdinalIgnoreCase));

        if (exportService is null)
            throw new InvalidOperationException($"Unsupported export format: {request.Format}");

        var data = await exportService.ExportAsync(result.ReportName, result.Columns, result.Rows, ct);

        return new ExportResult
        {
            FileName = $"{result.ReportName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.{request.Format}",
            ContentType = exportService.ContentType,
            Data = data
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

public sealed record ExportResult
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public byte[] Data { get; init; } = [];
}
