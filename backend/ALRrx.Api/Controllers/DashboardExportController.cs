using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Infrastructure.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/dashboard-export")]
[Authorize]
public sealed class DashboardExportController : ControllerBase
{
    private readonly ExportDashboardUseCase _exportDashboard;
    private readonly IDashboardExcelService _exportExcel;

    public DashboardExportController(ExportDashboardUseCase exportDashboard, IDashboardExcelService exportExcel)
    {
        _exportDashboard = exportDashboard;
        _exportExcel = exportExcel;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> ExportPdf([FromBody] TimeFilterDto filter, CancellationToken ct = default)
    {
        var pdfBytes = await _exportDashboard.ExecuteAsync(filter, ct);
        var fileName = $"ALTRX_Dashboard_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    [HttpPost("excel")]
    public async Task<IActionResult> ExportExcel([FromBody] TimeFilterDto filter, CancellationToken ct = default)
    {
        var data = await _exportDashboard.BuildDataAsync(filter, ct);
        var excelBytes = _exportExcel.GenerateDashboardExcel(data);
        var fileName = $"ALTRX_Dashboard_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
        return File(excelBytes, _exportExcel.ContentType, fileName);
    }
}