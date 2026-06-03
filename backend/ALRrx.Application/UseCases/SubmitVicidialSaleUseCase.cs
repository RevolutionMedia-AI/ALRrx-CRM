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

    public async Task<int> ExecuteAsync(VicidialSaleClientRequest request, VicidialFormIdentity identity, CancellationToken ct = default)
    {
        if (identity is null || string.IsNullOrWhiteSpace(identity.User) || string.IsNullOrWhiteSpace(identity.Name))
            throw new UnauthorizedAccessException("Invalid identity");
        if (string.IsNullOrWhiteSpace(request.ClientName))
            throw new ArgumentException("ClientName is required");
        if (string.IsNullOrWhiteSpace(request.ClientEmail))
            throw new ArgumentException("ClientEmail is required");
        if (string.IsNullOrWhiteSpace(request.ClientPhone))
            throw new ArgumentException("ClientPhone is required");
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero");
        if (!BundleTypeExtensions.TryParseBundle(request.Bundle, out var bundleType))
            throw new ArgumentException($"Invalid bundle: '{request.Bundle}'. Allowed: GLP-1 1/3/6/12 Months, GLP-1/GIP 1/3/6/12 Months");

        var bundleDisplayName = bundleType.ToDisplayName();

        var saleRequest = new VicidialSaleRequest
        {
            SalesRep = identity.Name,
            SaleDate = request.SaleDate,
            ClientPhone = request.ClientPhone,
            ClientName = request.ClientName,
            ClientEmail = request.ClientEmail,
            Bundle = request.Bundle,
            Amount = request.Amount,
        };

        var newId = await _repo.InsertAsync(saleRequest, bundleDisplayName, ct);
        _logger.LogInformation("Vicidial sale #{Id} submitted: user={User}, rep={Rep}, bundle={Bundle}, ${Amount}", newId, identity.User, identity.Name, bundleDisplayName, request.Amount);
        return newId;
    }
}
