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
        if (request.LeadId is not null and <= 0)
            throw new ArgumentException("LeadId must be greater than zero if provided");
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
        if (!BundleTypeExtensions.TryParseBundle(request.Bundle, out var bundleType))
            throw new ArgumentException($"Invalid bundle: '{request.Bundle}'. Allowed: GLP-1 1/3/6/12 Months, GLP-1/GIP 1/3/6/12 Months");

        var bundleDisplayName = bundleType.ToDisplayName();

        var newId = await _repo.InsertAsync(request, bundleDisplayName, ct);
        var source = request.LeadId.HasValue ? "VicidialForm" : "ManualForm";
        _logger.LogInformation("Vicidial sale #{Id} submitted: leadId={LeadId}, rep={Rep}, bundle={Bundle}, ${Amount}, source={Source}",
            newId, request.LeadId, request.SalesRep, bundleDisplayName, request.Amount, source);
        return newId;
    }
}
