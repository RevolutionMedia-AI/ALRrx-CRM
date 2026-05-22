using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/dashboard-export")]
[Authorize]
public sealed class DashboardExportController : ControllerBase
{
    private readonly ExportDashboardUseCase _exportDashboard;

    public DashboardExportController(ExportDashboardUseCase exportDashboard)
    {
        _exportDashboard = exportDashboard;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> ExportPdf([FromBody] TimeFilterDto filter, CancellationToken ct = default)
    {
        var pdfBytes = await _exportDashboard.ExecuteAsync(filter, ct);
        var fileName = $"ALTRX_Dashboard_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }
}