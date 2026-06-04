namespace Slice.Application.Interfaces;

/// <summary>
/// Sends transactional emails on behalf of Slice (currently via the Resend API).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an HTML email to <paramref name="toEmail"/> with the given subject and body.
    /// Optionally attaches a file from <paramref name="attachmentPath"/>.
    /// </summary>
    Task SendReportAsync(string toEmail, string subject, string htmlBody,
        string? attachmentPath = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches the report identified by <paramref name="reportId"/>, builds an HTML metrics
    /// summary, and sends it with the merged XLSX attached to <paramref name="toEmail"/>.
    /// </summary>
    Task SendMetricsEmailAsync(string toEmail, string reportId, CancellationToken ct = default);
}
