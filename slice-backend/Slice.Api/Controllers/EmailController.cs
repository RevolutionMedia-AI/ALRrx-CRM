using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Application.Interfaces;

namespace Slice.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EmailController : ControllerBase
{
    private readonly IEmailService _emailService;

    public EmailController(IEmailService emailService) => _emailService = emailService;

    [HttpPost("send-report")]
    public async Task<IActionResult> SendReport([FromBody] SendReportEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail) || string.IsNullOrWhiteSpace(request.ReportId))
            return BadRequest(new { error = "ToEmail and ReportId are required." });

        await _emailService.SendMetricsEmailAsync(request.ToEmail, request.ReportId, ct);
        return Ok(new { message = $"Report sent to {request.ToEmail}" });
    }
}
