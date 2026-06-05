using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IActiveAgentsRepository
{
    Task<List<ActiveAltrxAgentDto>> GetActiveAltrxAgentsAsync(CancellationToken ct = default);
    Task<ActiveAltrxAgentDto?> GetByUserAsync(string user, CancellationToken ct = default);
}
