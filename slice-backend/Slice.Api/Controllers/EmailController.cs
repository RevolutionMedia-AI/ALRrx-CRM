using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Application.Interfaces;

namespace Slice.Api.Controllers;

/// <summary>
/// Gestiona el envío de reportes por correo electrónico a través del servicio de email configurado (Resend).
/// Todos los endpoints requieren autenticación JWT.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class EmailController : ControllerBase
{
    private readonly IEmailService _emailService;

    public EmailController(IEmailService emailService) => _emailService = emailService;

    /// <summary>
    /// Envía el reporte indicado por email al destinatario especificado.
    /// El cuerpo del correo incluye un resumen HTML del Daily Global y el archivo XLSX adjunto.
    /// </summary>
    /// <remarks>
    /// El campo <c>Subject</c> del request es ignorado actualmente; el asunto se genera
    /// automáticamente a partir de la fecha del reporte.
    /// </remarks>
    [HttpPost("send-report")]
    public async Task<IActionResult> SendReport([FromBody] SendReportEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ToEmail) || string.IsNullOrWhiteSpace(request.ReportId))
            return BadRequest(new { error = "ToEmail and ReportId are required." });

        await _emailService.SendMetricsEmailAsync(request.ToEmail, request.ReportId, ct);
        return Ok(new { message = $"Report sent to {request.ToEmail}" });
    }
}
