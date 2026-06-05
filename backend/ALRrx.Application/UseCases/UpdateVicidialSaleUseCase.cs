using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class UpdateVicidialSaleUseCase
{
    public static readonly HashSet<string> AllowedEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "jessica.duarte@revolutionmedia.ai",
        "silverio.arellano@revolutionmedia.ai",
        "kevin.escalante@revolutionmedia.ai",
    };

    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<UpdateVicidialSaleUseCase> _logger;

    public UpdateVicidialSaleUseCase(IVicidialSalesRepository repo, ILogger<UpdateVicidialSaleUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(int id, VicidialSaleUpdateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.EditorEmail))
            throw new UnauthorizedAccessException("Editor email is required");
        if (!AllowedEditors.Contains(request.EditorEmail.Trim()))
            throw new UnauthorizedAccessException($"User '{request.EditorEmail}' is not allowed to edit sales");

        if (request.LeadId.HasValue && request.LeadId.Value <= 0)
            throw new ArgumentException("LeadId must be greater than zero");

        if (request.Amount.HasValue && request.Amount.Value <= 0)
            throw new ArgumentException("Amount must be greater than zero");

        var bundleType = default(ALRrx.Application.DTOs.BundleType);
        var hasBundle = !string.IsNullOrWhiteSpace(request.Bundle);
        if (hasBundle && !BundleTypeExtensions.TryParseBundle(request.Bundle, out bundleType))
            throw new ArgumentException($"Invalid bundle: '{request.Bundle}'");

        var bundleDisplayName = hasBundle ? bundleType.ToDisplayName() : string.Empty;

        var updated = await _repo.UpdateAsync(id, request, bundleDisplayName, ct);
        if (updated)
        {
            _logger.LogInformation("Vicidial sale #{Id} updated by authorized editor {Email}", id, request.EditorEmail);
        }
        return updated;
    }
}
