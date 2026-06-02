using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetVicidialSalesUseCase
{
    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<GetVicidialSalesUseCase> _logger;

    public GetVicidialSalesUseCase(IVicidialSalesRepository repo, ILogger<GetVicidialSalesUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<List<VicidialSaleDto>> ExecuteAsync(
        string salesRep,
        DateTime? from,
        DateTime? to,
        int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(salesRep))
            throw new ArgumentException("SalesRep is required");

        return await _repo.GetBySalesRepAsync(salesRep.Trim(), from, to, Math.Clamp(limit, 1, 200), ct);
    }
}
