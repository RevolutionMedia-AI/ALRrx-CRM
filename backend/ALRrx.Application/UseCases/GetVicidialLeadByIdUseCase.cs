using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetVicidialLeadByIdUseCase
{
    private readonly IVicidialLeadRepository _repo;
    private readonly ILogger<GetVicidialLeadByIdUseCase> _logger;

    public GetVicidialLeadByIdUseCase(
        IVicidialLeadRepository repo,
        ILogger<GetVicidialLeadByIdUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<LeadLookupResult> ExecuteAsync(int leadId, CancellationToken ct = default)
    {
        if (leadId <= 0)
            return LeadLookupResult.InvalidInputResult("lead_id must be a positive integer");

        try
        {
            var lead = await _repo.GetByIdAsync(leadId, ct);
            if (lead == null)
            {
                _logger.LogInformation("VICIdial lead #{LeadId} not found", leadId);
                return LeadLookupResult.NotFoundResult(leadId);
            }

            _logger.LogInformation("VICIdial lead #{LeadId} resolved: {First} {Last}", leadId, lead.FirstName, lead.LastName);
            return LeadLookupResult.FoundResult(lead);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout while querying VICIdial lead #{LeadId}", leadId);
            return LeadLookupResult.ConnectionErrorResult("VICIdial database connection timed out");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Database connection error while querying VICIdial lead #{LeadId}", leadId);
            return LeadLookupResult.ConnectionErrorResult("Cannot reach VICIdial database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while querying VICIdial lead #{LeadId}", leadId);
            return LeadLookupResult.ConnectionErrorResult("Unexpected error while contacting VICIdial");
        }
    }
}
