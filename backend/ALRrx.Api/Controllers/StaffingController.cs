using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class StaffingController : ControllerBase
{
    private readonly GetReportUseCase _getReport;
    private readonly TimeFilterValidator _validator;

    public StaffingController(GetReportUseCase getReport, TimeFilterValidator validator)
    {
        _getReport = getReport;
        _validator = validator;
    }

    [HttpGet]
    public async Task<ActionResult<ReportDto>> GetStaffing(CancellationToken ct = default)
    {
        var filter = new TimeFilterDto { Period = "Today" };
        var result = await _getReport.ExecuteAsync("staffing", filter, ct);
        return Ok(result);
    }

    [HttpGet("leaderboard")]
    public async Task<ActionResult<ReportDto>> GetLeaderboard(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        var filter = new TimeFilterDto { Period = period, CustomStart = customStart, CustomEnd = customEnd };
        var v = await _validator.ValidateAsync(filter, ct);
        if (!v.IsValid) return BadRequest(v.Errors);
        var result = await _getReport.ExecuteAsync("agent_leaderboard", filter, ct);
        return Ok(result);
    }

    [HttpGet("queue-metrics")]
    public async Task<ActionResult<ReportDto>> GetQueueMetrics(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        var filter = new TimeFilterDto { Period = period, CustomStart = customStart, CustomEnd = customEnd };
        var v = await _validator.ValidateAsync(filter, ct);
        if (!v.IsValid) return BadRequest(v.Errors);
        var result = await _getReport.ExecuteAsync("queue_metrics", filter, ct);
        return Ok(result);
    }

    [HttpGet("agent/{user}/history")]
    public async Task<ActionResult<ReportDto>> GetAgentHistory(
        string user,
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(user))
            return BadRequest(new { error = "user is required" });
        var filter = new TimeFilterDto { Period = period, CustomStart = customStart, CustomEnd = customEnd };
        var v = await _validator.ValidateAsync(filter, ct);
        if (!v.IsValid) return BadRequest(v.Errors);
        var result = await _getReport.ExecuteAsync($"agent_history:{user}", filter, ct);
        return Ok(result);
    }
}
