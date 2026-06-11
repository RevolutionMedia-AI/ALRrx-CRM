using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
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
    private readonly ILogger<DashboardExportController> _logger;

    public DashboardExportController(ExportDashboardUseCase exportDashboard, IDashboardExcelService exportExcel, ILogger<DashboardExportController> logger)
    {
        _exportDashboard = exportDashboard;
        _exportExcel = exportExcel;
        _logger = logger;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> ExportPdf([FromBody] TimeFilterDto filter, CancellationToken ct = default)
    {
        try
        {
            var pdfBytes = await _exportDashboard.ExecuteAsync(filter, ct);
            var fileName = $"ALTRX_Dashboard_{TimeZoneHelper.NowPstString("yyyyMMdd_HHmmss")}_PST.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PDF for period {Period}", filter.Period);
            return StatusCode(500, new { error = "Error generating PDF", detail = ex.Message });
        }
    }

    [HttpPost("excel")]
    public async Task<IActionResult> ExportExcel([FromBody] TimeFilterDto filter, CancellationToken ct = default)
    {
        try
        {
            var data = await _exportDashboard.BuildDataAsync(filter, ct);
            var excelBytes = _exportExcel.GenerateDashboardExcel(data);
            var fileName = $"ALTRX_Dashboard_{TimeZoneHelper.NowPstString("yyyyMMdd_HHmmss")}_PST.xlsx";
            return File(excelBytes, _exportExcel.ContentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Excel for period {Period}", filter.Period);
            return StatusCode(500, new { error = "Error generating Excel", detail = ex.Message });
        }
    }
}