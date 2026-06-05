using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IVicidialLeadRepository
{
    Task<VicidialLeadDto?> GetByIdAsync(int leadId, CancellationToken ct = default);

    Task<Dictionary<int, VicidialLeadDto>> GetByIdsAsync(IEnumerable<int> leadIds, CancellationToken ct = default);
}
