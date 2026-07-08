using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IEmailService
{
    Task<EmailResult> SendAccountPendingAsync(string toEmail, string toName, CancellationToken ct = default);
    Task<EmailResult> SendAccountApprovedAsync(string toEmail, string toName, string roleName, CancellationToken ct = default);
    Task<EmailResult> SendAccountRejectedAsync(string toEmail, string toName, string reason, CancellationToken ct = default);
    Task<EmailResult> SendAccountSuspendedAsync(string toEmail, string toName, string reason, CancellationToken ct = default);
    Task<EmailResult> SendPlatformAccessGrantedAsync(
        string toEmail,
        string toName,
        string roleName,
        string platformName,
        CancellationToken ct = default);
}
