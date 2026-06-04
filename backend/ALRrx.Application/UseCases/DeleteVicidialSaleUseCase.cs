using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class DeleteVicidialSaleUseCase
{
    public static readonly HashSet<string> AllowedEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "jessica.duarte@revolutionmedia.ai",
        "silverio.arellano@revolutionmedia.ai",
        "kevin.escalante@revolutionmedia.ai",
    };

    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<DeleteVicidialSaleUseCase> _logger;

    public DeleteVicidialSaleUseCase(IVicidialSalesRepository repo, ILogger<DeleteVicidialSaleUseCase> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task ExecuteAsync(int id, string editorEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(editorEmail))
            throw new UnauthorizedAccessException("Editor email is required");
        if (!AllowedEditors.Contains(editorEmail.Trim()))
            throw new UnauthorizedAccessException($"User '{editorEmail}' is not allowed to delete sales");

        var deleted = await _repo.DeleteAsync(id, ct);
        if (!deleted)
            throw new KeyNotFoundException($"Sale #{id} not found");

        _logger.LogInformation("Vicidial sale #{Id} deleted by authorized editor {Email}", id, editorEmail);
    }
}
