using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IVicidialSalesRepository
{
    Task EnsureTableAsync(CancellationToken ct = default);
    Task<int> InsertAsync(VicidialSaleRequest request, CancellationToken ct = default);
    Task<List<VicidialSaleDto>> GetBySalesRepAsync(string salesRep, DateTime? from, DateTime? to, int limit, CancellationToken ct = default);
    Task<List<VicidialSaleDto>> GetAllAsync(DateTime? from, DateTime? to, int limit, CancellationToken ct = default);
}
