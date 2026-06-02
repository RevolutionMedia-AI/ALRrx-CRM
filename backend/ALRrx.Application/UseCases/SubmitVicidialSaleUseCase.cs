using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class SubmitVicidialSaleUseCase
{
    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<SubmitVicidialSaleUseCase> _logger;

    public SubmitVicidialSaleUseCase(IVicidialSalesRepository repo, ILogger<SubmitVicidialSaleUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<int> ExecuteAsync(VicidialSaleRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.SalesRep))
            throw new ArgumentException("SalesRep is required");
        if (string.IsNullOrWhiteSpace(request.ClientName))
            throw new ArgumentException("ClientName is required");
        if (string.IsNullOrWhiteSpace(request.ClientEmail))
            throw new ArgumentException("ClientEmail is required");
        if (string.IsNullOrWhiteSpace(request.ClientPhone))
            throw new ArgumentException("ClientPhone is required");
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero");

        var newId = await _repo.InsertAsync(request, ct);
        _logger.LogInformation("Vicidial sale #{Id} submitted: rep={Rep}, bundle={Bundle}, ${Amount}", newId, request.SalesRep, request.Bundle, request.Amount);
        return newId;
    }
}
