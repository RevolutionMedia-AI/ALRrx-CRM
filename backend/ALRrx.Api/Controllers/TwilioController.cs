using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/twilio")]
[Authorize(Roles = "Admin")]
public class TwilioController : ControllerBase
{
    private readonly ITwilioService _twilio;
    private readonly ILogger<TwilioController> _logger;

    public TwilioController(ITwilioService twilio, ILogger<TwilioController> logger)
    {
        _twilio = twilio;
        _logger = logger;
    }

    /// <summary>Resumen de costos. Period: today|week|month|custom</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] string period = "today",
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken ct = default)
    {
        try
        {
            var summary = await _twilio.GetSummaryAsync(period, startDate, endDate, ct);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Twilio summary");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("calls/recent")]
    public async Task<IActionResult> GetRecentCalls([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var calls = await _twilio.GetRecentCallsAsync(limit, ct);
            return Ok(calls);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recent Twilio calls");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("costs/daily")]
    public async Task<IActionResult> GetDailyCosts([FromQuery] int days = 30, CancellationToken ct = default)
    {
        try
        {
            var costs = await _twilio.GetDailyCostsAsync(days, ct);
            return Ok(costs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching daily Twilio costs");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
