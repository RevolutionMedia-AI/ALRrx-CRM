namespace Slice.Application.Interfaces;

public interface IEmailService
{
    Task SendReportAsync(string toEmail, string subject, string htmlBody, string? attachmentPath = null, CancellationToken ct = default);
    Task SendMetricsEmailAsync(string toEmail, string reportId, CancellationToken ct = default);
}
