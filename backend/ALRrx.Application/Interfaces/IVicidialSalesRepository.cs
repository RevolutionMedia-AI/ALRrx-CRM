using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IVicidialSalesRepository
{
    Task EnsureTableAsync(CancellationToken ct = default);
    Task<int> InsertAsync(VicidialSaleRequest request, string bundleDisplayName, CancellationToken ct = default);
    Task<List<VicidialSaleDto>> GetBySalesRepAsync(string salesRep, string? from, string? to, int limit, CancellationToken ct = default);
    Task<List<VicidialSaleDto>> GetByLeadIdAsync(int leadId, int limit, CancellationToken ct = default);
    Task<List<VicidialSaleDto>> GetAllAsync(string? from, string? to, int limit, CancellationToken ct = default);
    Task<SalesSummaryDto> GetSummaryAsync(string? from, string? to, int limit, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, VicidialSaleUpdateRequest request, string bundleDisplayName, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
