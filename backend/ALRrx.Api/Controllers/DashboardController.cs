using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly GetDashboardDataUseCase _getDashboardData;
    private readonly TimeFilterValidator _validator;

    public DashboardController(
        GetDashboardDataUseCase getDashboardData,
        TimeFilterValidator validator)
    {
        _getDashboardData = getDashboardData;
        _validator = validator;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        var filter = new TimeFilterDto
        {
            Period = period,
            CustomStart = customStart,
            CustomEnd = customEnd
        };

        var validationResult = await _validator.ValidateAsync(filter, ct);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        var result = await _getDashboardData.ExecuteAsync(filter, ct);
        return Ok(result);
    }
}
