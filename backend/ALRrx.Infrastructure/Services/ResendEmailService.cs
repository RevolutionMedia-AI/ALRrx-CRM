using System.Net.Http.Headers;
using System.Reflection;
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
    private readonly string _logoUrl;
    private readonly bool _enabled;
    private readonly string? _platformAccessGrantedTemplate;

    public ResendEmailService(HttpClient http, IConfiguration config, ILogger<ResendEmailService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["Resend:ApiKey"] ?? string.Empty;
        _fromAddress = config["Resend:FromAddress"] ?? "noreply@revolutionmedia.ai";
        _fromName = config["Resend:FromName"] ?? "RevolutionMedia.ai";
        _loginUrl = config["Resend:LoginUrl"] ?? "https://alrrx.ai/login";
        _logoUrl = config["Resend:LogoUrl"] ?? "https://alrrx.ai/logo.png";
        _enabled = !string.IsNullOrEmpty(_apiKey);

        if (_enabled)
        {
            _http.BaseAddress = new Uri("https://api.resend.com/");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        // Load the platform-access-granted template from the embedded
        // resource once at startup. If the resource can't be found we
        // log a warning and fall back to a simple plain-text subject —
        // the email service still works, just with the basic subject
        // line instead of the styled HTML.
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("platform-access-granted.html", StringComparison.OrdinalIgnoreCase));
            if (resourceName is not null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                _platformAccessGrantedTemplate = reader.ReadToEnd();
            }
            else
            {
                _logger.LogWarning("[Resend] Embedded resource 'platform-access-granted.html' not found — falling back to plain-text email");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Resend] Failed to load platform-access-granted.html embedded resource");
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
            "Your ALRrx account was suspended",
            $"<p>Hello <strong>{HtmlEncode(toName)}</strong>,</p>" +
            $"<p>Your account in ALRrx CRM has been suspended.</p>" +
            $"<p><strong>Reason:</strong> {HtmlEncode(reason)}</p>" +
            $"<p>Contact the administrator for more information.</p>",
            ct);

    public Task<EmailResult> SendPlatformAccessGrantedAsync(
        string toEmail,
        string toName,
        string roleName,
        string platformName,
        CancellationToken ct = default)
    {
        var subject = $"Your access to {platformName} is now active — RevolutionMedia.ai";

        if (_platformAccessGrantedTemplate is null)
        {
            // Fallback when the embedded resource is missing: send a
            // simple plain-text body so the user still gets the
            // notification, just without the styled HTML.
            return SendAsync(toEmail, toName, subject,
                $"<p>Hello <strong>{HtmlEncode(toName)}</strong>,</p>" +
                $"<p>You have been granted <strong>{HtmlEncode(roleName)}</strong> access to " +
                $"<strong>{HtmlEncode(platformName)}</strong> on RevolutionMedia.ai.</p>" +
                $"<p>Sign in with your @revolutionmedia.ai Google account to get started:</p>" +
                $"<p><a href=\"{_loginUrl}\">{_loginUrl}</a></p>",
                ct);
        }

        var body = _platformAccessGrantedTemplate
            .Replace("{{UserFullName}}", HtmlEncode(toName))
            .Replace("{{UserEmail}}",    HtmlEncode(toEmail))
            .Replace("{{RoleName}}",     HtmlEncode(roleName))
            .Replace("{{PlatformName}}", HtmlEncode(platformName))
            .Replace("{{LoginUrl}}",     HtmlEncode(_loginUrl))
            .Replace("{{LogoUrl}}",      HtmlEncode(_logoUrl));

        return SendAsync(toEmail, toName, subject, body, ct);
    }

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
