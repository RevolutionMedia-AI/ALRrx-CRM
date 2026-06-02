using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Email;

/// <summary>
/// Sends transactional emails via the <see href="https://resend.com">Resend API</see>.
/// Configuration is read from <c>appsettings.json → Resend:ApiKey</c> and <c>Resend:FromEmail</c>.
/// The HTTP client is provided by the named factory entry <c>"Resend"</c> (registered in DI).
/// </summary>
public sealed class ResendEmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration     _config;
    private readonly IReportRepository  _reportRepo;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IReportRepository reportRepo,
        ILogger<ResendEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config            = config;
        _reportRepo        = reportRepo;
        _logger            = logger;
    }

    /// <inheritdoc/>
    public async Task SendReportAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? attachmentPath = null,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("Resend");

        var payload = new ResendEmailPayload
        {
            From    = _config["Resend:FromEmail"]!,
            To      = [toEmail],
            Subject = subject,
            Html    = htmlBody,
        };

        // Attach the XLSX file if a valid path is provided.
        if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
        {
            var bytes = await File.ReadAllBytesAsync(attachmentPath, ct);
            payload.Attachments =
            [
                new ResendAttachment
                {
                    Filename = Path.GetFileName(attachmentPath),
                    Content  = Convert.ToBase64String(bytes),
                }
            ];
        }

        var response = await client.PostAsJsonAsync("emails", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Resend API error {Status}: {Error}", response.StatusCode, error);
            throw new InvalidOperationException($"Failed to send email: {error}");
        }

        _logger.LogInformation("Email sent to {To} via Resend", toEmail);
    }

    /// <inheritdoc/>
    public async Task SendMetricsEmailAsync(string toEmail, string reportId, CancellationToken ct = default)
    {
        var report = await _reportRepo.GetByIdAsync(reportId)
            ?? throw new KeyNotFoundException($"Report {reportId} not found");

        var html = BuildMetricsHtml(report);
        await SendReportAsync(
            toEmail,
            subject:        $"Slice Daily Report — {report.ReportDate:yyyy-MM-dd}",
            htmlBody:       html,
            attachmentPath: report.MergedXlsxPath,
            ct);
    }

    // ─── HTML template ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a responsive HTML email with a Daily Global summary table
    /// and the Slice brand header.
    /// </summary>
    private static string BuildMetricsHtml(SliceReport report)
    {
        var rows = string.Join("", report.DailyGlobal.Select(g =>
            $"""
            <tr>
              <td style="padding:8px;border:1px solid #ddd">{g.Pod}</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.Queued}</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.Handled}</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.MissedCalls}</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.ConvPct:F1}%</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.OrderCount}</td>
              <td style="padding:8px;border:1px solid #ddd;text-align:right">{g.RefundedOrders}</td>
            </tr>
            """));

        return $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"/></head>
            <body style="font-family:Arial,sans-serif;color:#333;max-width:800px;margin:0 auto">
              <div style="background:#1f77b4;color:#fff;padding:24px;border-radius:8px 8px 0 0">
                <h1 style="margin:0;font-size:22px">Slice Daily Report</h1>
                <p style="margin:4px 0 0;opacity:.8">{report.ReportDate:dddd, MMMM d yyyy}</p>
              </div>
              <div style="padding:24px;background:#f9f9f9">
                <h2 style="color:#1f77b4;margin-top:0">Daily Global Summary</h2>
                <table style="width:100%;border-collapse:collapse;background:#fff">
                  <thead>
                    <tr style="background:#1f77b4;color:#fff">
                      <th style="padding:10px;text-align:left">Pod</th>
                      <th style="padding:10px;text-align:right">Queued</th>
                      <th style="padding:10px;text-align:right">Handled</th>
                      <th style="padding:10px;text-align:right">Missed</th>
                      <th style="padding:10px;text-align:right">Conv %</th>
                      <th style="padding:10px;text-align:right">Orders</th>
                      <th style="padding:10px;text-align:right">Refunds</th>
                    </tr>
                  </thead>
                  <tbody>{rows}</tbody>
                </table>
                <p style="margin-top:24px;font-size:13px;color:#777">
                  Generated by Slice CRM — {report.GeneratedAt:yyyy-MM-dd HH:mm} UTC
                </p>
              </div>
            </body>
            </html>
            """;
    }

    // ─── Resend API payload models ────────────────────────────────────────────

    /// <summary>Request body sent to the Resend <c>POST /emails</c> endpoint.</summary>
    private sealed class ResendEmailPayload
    {
        public string             From        { get; set; } = string.Empty;
        public List<string>       To          { get; set; } = [];
        public string             Subject     { get; set; } = string.Empty;
        public string             Html        { get; set; } = string.Empty;
        public List<ResendAttachment>? Attachments { get; set; }
    }

    /// <summary>A single base-64-encoded file attachment for the Resend API.</summary>
    private sealed class ResendAttachment
    {
        public string Filename { get; set; } = string.Empty;
        /// <summary>Base-64 encoded file content.</summary>
        public string Content  { get; set; } = string.Empty;
    }
}
