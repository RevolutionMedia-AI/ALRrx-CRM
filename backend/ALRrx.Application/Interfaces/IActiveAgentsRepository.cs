using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IActiveAgentsRepository
{
    Task<List<ActiveAltrxAgentDto>> GetActiveAltrxAgentsAsync(CancellationToken ct = default);
}
