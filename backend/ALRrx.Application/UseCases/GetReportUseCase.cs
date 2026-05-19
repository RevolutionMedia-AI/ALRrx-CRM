using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;
using AutoMapper;

namespace ALRrx.Application.UseCases;

public sealed class GetReportUseCase
{
    private readonly IQueryService _queryService;
    private readonly IMapper _mapper;

    public GetReportUseCase(IQueryService queryService, IMapper mapper)
    {
        _queryService = queryService;
        _mapper = mapper;
    }

    public async Task<ReportDto> ExecuteAsync(string reportId, TimeFilterDto filter, CancellationToken ct = default)
    {
        var timeRange = BuildTimeRange(filter);
        var result = await _queryService.ExecuteQueryAsync(reportId, timeRange, ct);
        return _mapper.Map<ReportDto>(result);
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
