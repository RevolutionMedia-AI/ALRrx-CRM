using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetAllVicidialSalesUseCase
{
    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<GetAllVicidialSalesUseCase> _logger;

    public GetAllVicidialSalesUseCase(IVicidialSalesRepository repo, ILogger<GetAllVicidialSalesUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<List<VicidialSaleDto>> ExecuteAsync(
        DateTime? from,
        DateTime? to,
        int limit = 200,
        CancellationToken ct = default)
    {
        return await _repo.GetAllAsync(from, to, Math.Clamp(limit, 1, 1000), ct);
    }
}
