using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Interfaces;
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
        var timeRange = TimeFilterHelper.BuildTimeRange(filter);
        var result = await _queryService.ExecuteQueryAsync(reportId, timeRange, ct);
        return _mapper.Map<ReportDto>(result);
    }
}