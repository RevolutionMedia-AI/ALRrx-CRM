using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class StaffingController : ControllerBase
{
    private readonly GetReportUseCase _getReport;

    public StaffingController(GetReportUseCase getReport)
    {
        _getReport = getReport;
    }

    [HttpGet]
    public async Task<ActionResult<ReportDto>> GetStaffing(CancellationToken ct = default)
    {
        var filter = new TimeFilterDto { Period = "Today" };
        var result = await _getReport.ExecuteAsync("staffing", filter, ct);
        return Ok(result);
    }
}
