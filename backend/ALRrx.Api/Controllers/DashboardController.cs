using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly GetDashboardDataUseCase _getDashboardData;
    private readonly TimeFilterValidator _validator;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        GetDashboardDataUseCase getDashboardData,
        TimeFilterValidator validator,
        ILogger<DashboardController> logger)
    {
        _getDashboardData = getDashboardData;
        _validator = validator;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(">>> DASHBOARD REQUEST >>> period: {Period} | customStart: {CustomStart:dd/MM/yyyy HH:mm:ss} | customEnd: {CustomEnd:dd/MM/yyyy HH:mm:ss}", period, customStart, customEnd);

        var filter = new TimeFilterDto
        {
            Period = period,
            CustomStart = customStart,
            CustomEnd = customEnd
        };

        _logger.LogInformation(">>> FILTER CREATED >>> Period: {Period} | CustomStart: {CustomStart:dd/MM/yyyy HH:mm:ss} | CustomEnd: {CustomEnd:dd/MM/yyyy HH:mm:ss}", filter.Period, filter.CustomStart, filter.CustomEnd);

        var validationResult = await _validator.ValidateAsync(filter, ct);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
            return BadRequest(validationResult.Errors);
        }

        var result = await _getDashboardData.ExecuteAsync(filter, ct);
        return Ok(result);
    }
}
