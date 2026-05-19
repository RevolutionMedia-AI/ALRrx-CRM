using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ExportController : ControllerBase
{
    private readonly ExportReportUseCase _exportReport;

    public ExportController(ExportReportUseCase exportReport)
    {
        _exportReport = exportReport;
    }

    [HttpPost]
    public async Task<IActionResult> Export(
        [FromBody] ExportRequestDto request,
        CancellationToken ct = default)
    {
        var result = await _exportReport.ExecuteAsync(request, ct);
        return File(result.Data, result.ContentType, result.FileName);
    }
}
