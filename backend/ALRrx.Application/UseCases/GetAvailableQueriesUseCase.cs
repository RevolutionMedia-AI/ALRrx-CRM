using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Interfaces;
using AutoMapper;

namespace ALRrx.Application.UseCases;

public sealed class GetAvailableQueriesUseCase
{
    private readonly IQueryService _queryService;
    private readonly IMapper _mapper;

    public GetAvailableQueriesUseCase(IQueryService queryService, IMapper mapper)
    {
        _queryService = queryService;
        _mapper = mapper;
    }

    public IReadOnlyCollection<QueryDefinitionDto> Execute()
    {
        var queries = _queryService.GetAvailableQueries();
        return _mapper.Map<List<QueryDefinitionDto>>(queries);
    }
}
