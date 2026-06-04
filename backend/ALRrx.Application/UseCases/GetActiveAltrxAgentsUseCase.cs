using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetActiveAltrxAgentsUseCase
{
    private readonly IActiveAgentsRepository _repo;
    private readonly ILogger<GetActiveAltrxAgentsUseCase> _logger;

    public GetActiveAltrxAgentsUseCase(IActiveAgentsRepository repo, ILogger<GetActiveAltrxAgentsUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<List<ActiveAltrxAgentDto>> ExecuteAsync(CancellationToken ct = default)
    {
        var agents = await _repo.GetActiveAltrxAgentsAsync(ct);
        return agents
            .OrderBy(a => a.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
