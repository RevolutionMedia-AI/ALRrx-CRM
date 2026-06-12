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
        _logger.LogInformation("[Twilio] GetSummary called: period='{Period}' startDate={Start} endDate={End}", period, startDate, endDate);
        try
        {
            var summary = await _twilio.GetSummaryAsync(period, startDate, endDate, ct);
            _logger.LogInformation("[Twilio] GetSummary result: totalCost={Cost} calls={Calls} inbound={In} outbound={Out} periodStart={Start} periodEnd={End}",
                summary.TotalCost, summary.TotalCalls, summary.InboundCalls, summary.OutboundCalls, summary.PeriodStart, summary.PeriodEnd);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Twilio summary");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("calls/recent")]
    public async Task<IActionResult> GetRecentCalls(
        [FromQuery] string period = "today",
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            var calls = await _twilio.GetRecentCallsAsync(period, limit, ct);
            return Ok(calls);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching recent Twilio calls");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("costs/daily")]
    public async Task<IActionResult> GetDailyCosts(
        [FromQuery] string period = "today",
        CancellationToken ct = default)
    {
        try
        {
            var costs = await _twilio.GetDailyCostsAsync(period, ct);
            return Ok(costs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching daily Twilio costs");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
