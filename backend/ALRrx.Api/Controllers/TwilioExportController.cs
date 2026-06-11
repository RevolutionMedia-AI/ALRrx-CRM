using ALRrx.Application.Helpers;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/twilio-export")]
[Authorize(Roles = "Admin")]
public sealed class TwilioExportController : ControllerBase
{
    private readonly TwilioExportUseCase _useCase;
    private readonly ITwilioPdfService _pdf;
    private readonly ITwilioExcelService _excel;
    private readonly ILogger<TwilioExportController> _logger;

    public TwilioExportController(
        TwilioExportUseCase useCase,
        ITwilioPdfService pdf,
        ITwilioExcelService excel,
        ILogger<TwilioExportController> logger)
    {
        _useCase = useCase;
        _pdf = pdf;
        _excel = excel;
        _logger = logger;
    }

    [HttpPost("pdf")]
    public async Task<IActionResult> ExportPdf([FromBody] TwilioExportRequest req, CancellationToken ct = default)
    {
        try
        {
            var period = string.IsNullOrWhiteSpace(req?.Period) ? "today" : req.Period;
            var data = await _useCase.BuildDataAsync(period, ct);
            var bytes = _pdf.GenerateTwilioPdf(data);
            var fileName = $"ALRrx_TwilioCosts_{period}_{TimeZoneHelper.NowPstString("yyyyMMdd_HHmmss")}_PST.pdf";
            return File(bytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Twilio PDF for period {Period}", req?.Period);
            return StatusCode(500, new { error = "Error generating Twilio PDF", detail = ex.Message });
        }
    }

    [HttpPost("excel")]
    public async Task<IActionResult> ExportExcel([FromBody] TwilioExportRequest req, CancellationToken ct = default)
    {
        try
        {
            var period = string.IsNullOrWhiteSpace(req?.Period) ? "today" : req.Period;
            var data = await _useCase.BuildDataAsync(period, ct);
            var bytes = _excel.GenerateTwilioExcel(data);
            var fileName = $"ALRrx_TwilioCosts_{period}_{TimeZoneHelper.NowPstString("yyyyMMdd_HHmmss")}_PST.xlsx";
            return File(bytes, _excel.ContentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Twilio Excel for period {Period}", req?.Period);
            return StatusCode(500, new { error = "Error generating Twilio Excel", detail = ex.Message });
        }
    }
}
