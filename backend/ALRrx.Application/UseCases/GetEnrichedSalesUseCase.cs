using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GetEnrichedSalesUseCase
{
    private const int DefaultByLeadLimit = 500;

    private readonly IVicidialSalesRepository _salesRepo;
    private readonly IVicidialLeadRepository _leadRepo;
    private readonly ILogger<GetEnrichedSalesUseCase> _logger;

    public GetEnrichedSalesUseCase(
        IVicidialSalesRepository salesRepo,
        IVicidialLeadRepository leadRepo,
        ILogger<GetEnrichedSalesUseCase> logger)
    {
        _salesRepo = salesRepo;
        _leadRepo = leadRepo;
        _logger = logger;
    }

    public async Task<List<VicidialSaleEnrichedDto>> ExecuteAsync(
        string? from = null,
        string? to = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        var sales = await _salesRepo.GetAllAsync(from, to, limit, ct);
        if (sales.Count == 0) return new List<VicidialSaleEnrichedDto>();

        var leadIds = sales
            .Where(s => s.LeadId.HasValue && s.LeadId.Value > 0)
            .Select(s => s.LeadId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, VicidialLeadDto> leadMap = new();
        if (leadIds.Count > 0)
        {
            try
            {
                leadMap = await _leadRepo.GetByIdsAsync(leadIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich {Count} sales with VICIdial lead data — continuing without enrichment", leadIds.Count);
            }
        }

        var enriched = new List<VicidialSaleEnrichedDto>(sales.Count);
        foreach (var sale in sales)
        {
            VicidialLeadDto? attachedLead = null;
            var leadFound = false;

            if (sale.LeadId.HasValue && sale.LeadId.Value > 0)
            {
                leadFound = leadMap.TryGetValue(sale.LeadId.Value, out attachedLead);
            }

            enriched.Add(new VicidialSaleEnrichedDto
            {
                Id = sale.Id,
                LeadId = sale.LeadId,
                SalesRep = sale.SalesRep,
                SaleDate = sale.SaleDate,
                ClientPhone = sale.ClientPhone,
                ClientName = sale.ClientName,
                ClientEmail = sale.ClientEmail,
                Bundle = sale.Bundle,
                Amount = sale.Amount,
                ConfirmationUrl = sale.ConfirmationUrl,
                CreatedAt = sale.CreatedAt,
                Lead = attachedLead,
                LeadFound = leadFound,
            });
        }

        return enriched;
    }

    public async Task<List<VicidialSaleEnrichedDto>> GetByLeadIdAsync(int leadId, CancellationToken ct = default)
    {
        if (leadId <= 0) throw new ArgumentException("leadId must be greater than zero");

        var sales = await _salesRepo.GetByLeadIdAsync(leadId, DefaultByLeadLimit, ct);
        if (sales.Count == 0) return new List<VicidialSaleEnrichedDto>();

        VicidialLeadDto? fetchedLead = null;
        try
        {
            fetchedLead = await _leadRepo.GetByIdAsync(leadId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch lead #{LeadId} for enrichment", leadId);
        }

        var leadAvailable = fetchedLead != null;
        var enriched = new List<VicidialSaleEnrichedDto>(sales.Count);
        foreach (var sale in sales)
        {
            enriched.Add(new VicidialSaleEnrichedDto
            {
                Id = sale.Id,
                LeadId = sale.LeadId,
                SalesRep = sale.SalesRep,
                SaleDate = sale.SaleDate,
                ClientPhone = sale.ClientPhone,
                ClientName = sale.ClientName,
                ClientEmail = sale.ClientEmail,
                Bundle = sale.Bundle,
                Amount = sale.Amount,
                ConfirmationUrl = sale.ConfirmationUrl,
                CreatedAt = sale.CreatedAt,
                Lead = fetchedLead,
                LeadFound = leadAvailable,
            });
        }
        return enriched;
    }
}
