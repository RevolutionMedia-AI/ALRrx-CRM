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
        string? salesRep,
        string? from,
        string? to,
        int limit = 50,
        CancellationToken ct = default)
    {
        if (salesRep != null && string.IsNullOrWhiteSpace(salesRep))
            throw new ArgumentException("SalesRep cannot be empty");

        var clamped = Math.Clamp(limit, 1, 1000);

        if (string.IsNullOrWhiteSpace(salesRep))
            return await _repo.GetAllAsync(from, to, clamped, ct);

        return await _repo.GetBySalesRepAsync(salesRep.Trim(), from, to, clamped, ct);
    }

    public async Task<SalesSummaryDto> ExecuteSummaryAsync(
        string? from,
        string? to,
        int limit = 500,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 1000);
        return await _repo.GetSummaryAsync(from, to, clamped, ct);
    }
}
