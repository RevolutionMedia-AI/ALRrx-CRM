using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ALRrx.Infrastructure.Services;

public sealed class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly ILogger<ResendEmailService> _logger;
    private readonly string _fromAddress;
    private readonly string _fromName;
    private readonly string _apiKey;
    private readonly string _loginUrl;
    private readonly bool _enabled;

    public ResendEmailService(HttpClient http, IConfiguration config, ILogger<ResendEmailService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["Resend:ApiKey"] ?? string.Empty;
        _fromAddress = config["Resend:FromAddress"] ?? "noreply@alrrx.ai";
        _fromName = config["Resend:FromName"] ?? "ALRrx CRM";
        _loginUrl = config["Resend:LoginUrl"] ?? "https://alrrx.ai/login";
        _enabled = !string.IsNullOrEmpty(_apiKey);

        if (_enabled)
        {
            _http.BaseAddress = new Uri("https://api.resend.com/");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.Timeout = TimeSpan.FromSeconds(10);
        }
    }

    public Task<EmailResult> SendAccountApprovedAsync(string toEmail, string toName, string roleName, CancellationToken ct = default)
        => SendAsync(toEmail, toName,
            "Tu cuenta de ALRrx fue aprobada",
            $"<p>Hola <strong>{HtmlEncode(toName)}</strong>,</p>" +
            $"<p>Tu cuenta en ALRrx CRM ha sido aprobada con el rol <strong>{HtmlEncode(roleName)}</strong>.</p>" +
            $"<p>Ya puedes iniciar sesión: <a href=\"{_loginUrl}\">{_loginUrl}</a></p>",
            ct);

    public Task<EmailResult> SendAccountRejectedAsync(string toEmail, string toName, string reason, CancellationToken ct = default)
        => SendAsync(toEmail, toName,
            "Tu solicitud a ALRrx fue rechazada",
            $"<p>Hola <strong>{HtmlEncode(toName)}</strong>,</p>" +
            $"<p>Lamentablemente tu solicitud de acceso a ALRrx CRM fue rechazada.</p>" +
            $"<p><strong>Motivo:</strong> {HtmlEncode(reason)}</p>" +
            $"<p>Si crees que es un error, contacta al administrador.</p>",
            ct);

    public Task<EmailResult> SendAccountSuspendedAsync(string toEmail, string toName, string reason, CancellationToken ct = default)
        => SendAsync(toEmail, toName,
            "Tu cuenta de ALRrx fue suspendida",
            $"<p>Hola <strong>{HtmlEncode(toName)}</strong>,</p>" +
            $"<p>Tu cuenta en ALRrx CRM ha sido suspendida.</p>" +
            $"<p><strong>Motivo:</strong> {HtmlEncode(reason)}</p>" +
            $"<p>Contacta al administrador para más información.</p>",
            ct);

    private async Task<EmailResult> SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct)
    {
        if (!_enabled)
        {
            _logger.LogWarning("[Resend] API key missing — skipping email to {Email} subject='{Subject}'", toEmail, subject);
            return new EmailResult(false, "Resend API key not configured");
        }

        var payload = new
        {
            from = $"{_fromName} <{_fromAddress}>",
            to = new[] { toEmail },
            subject,
            html = htmlBody
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync("emails", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var err = $"Resend returned {(int)response.StatusCode} {response.StatusCode}";
                _logger.LogError("[Resend] send failed to {Email} status={Status} body={Body}", toEmail, response.StatusCode, body);
                return new EmailResult(false, err);
            }
            _logger.LogInformation("[Resend] sent email to {Email} subject='{Subject}'", toEmail, subject);
            return new EmailResult(true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Resend] send threw for {Email} subject='{Subject}'", toEmail, subject);
            return new EmailResult(false, ex.Message);
        }
    }

    private static string HtmlEncode(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}
