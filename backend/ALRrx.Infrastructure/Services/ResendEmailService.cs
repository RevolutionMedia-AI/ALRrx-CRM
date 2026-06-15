using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    // All four transactional templates are loaded once at startup from
    // embedded resources. Each is HTML with {{Placeholder}} tokens that
    // we string-replace at send time. The templates are kept in
    // /Templates/*.html so designers can edit them without touching C#.
    private readonly Dictionary<string, string> _templates;

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

        _templates = LoadTemplates(logger);
    }

    /// <summary>
    /// Load every .html file under /Templates as an embedded resource.
    /// The key is the file name without extension (e.g. "account-approved"),
    /// which each Send method looks up to render its body.
    /// </summary>
    private static Dictionary<string, string> LoadTemplates(ILogger logger)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                // Resources are named like
                //   ALRrx.Infrastructure.Templates.account-approved.html
                const string suffix = ".Templates.";
                var idx = resourceName.IndexOf(suffix, StringComparison.Ordinal);
                if (idx < 0) continue;
                var fileName = resourceName[(idx + suffix.Length)..];
                if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) continue;
                var key = fileName[..^".html".Length];

                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                result[key] = reader.ReadToEnd();
            }
            logger.LogInformation("[Resend] loaded {Count} email template(s): {Keys}",
                result.Count, string.Join(", ", result.Keys));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Resend] failed to load embedded email templates");
        }
        return result;
    }

    /// <summary>
    /// Render a template by key, substituting every {{Token}} with its
    /// value. Falls back to a small plain-text body if the template is
    /// missing so the user still gets *some* email when the embedded
    /// resource fails to load.
    /// </summary>
    private Task<EmailResult> RenderOrFallback(
        string templateKey,
        string subject,
        IReadOnlyDictionary<string, string?> values,
        string fallbackBody)
    {
        if (!_templates.TryGetValue(templateKey, out var template))
        {
            _logger.LogWarning("[Resend] template '{Key}' not found — using plain-text fallback", templateKey);
            return SendAsync(values["UserEmail"] ?? "", values["UserFullName"] ?? "", subject, fallbackBody, default);
        }

        var body = Regex.Replace(template, @"\{\{(\w+)\}\}", match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var v) ? HtmlEncode(v ?? string.Empty) : match.Value;
        });

        return SendAsync(values["UserEmail"] ?? "", values["UserFullName"] ?? "", subject, body, default);
    }

    public Task<EmailResult> SendAccountApprovedAsync(string toEmail, string toName, string roleName, CancellationToken ct = default)
        => RenderOrFallback("account-approved",
            "Your account has been approved — RevolutionMedia.ai",
            new Dictionary<string, string?>
            {
                ["UserEmail"]    = toEmail,
                ["UserFullName"] = toName,
                ["RoleName"]     = roleName,
                ["LoginUrl"]     = _loginUrl,
                ["LogoUrl"]      = _logoUrl,
            },
            $"<p>Hello {HtmlEncode(toName)},</p>" +
            $"<p>Your account on RevolutionMedia.ai has been approved with the role <strong>{HtmlEncode(roleName)}</strong>.</p>" +
            $"<p>Sign in here: <a href=\"{_loginUrl}\">{_loginUrl}</a></p>");

    public Task<EmailResult> SendAccountRejectedAsync(string toEmail, string toName, string reason, CancellationToken ct = default)
        => RenderOrFallback("account-rejected",
            "Your account application has been rejected — RevolutionMedia.ai",
            new Dictionary<string, string?>
            {
                ["UserEmail"]    = toEmail,
                ["UserFullName"] = toName,
                ["Reason"]       = reason,
                ["LogoUrl"]      = _logoUrl,
            },
            $"<p>Hello {HtmlEncode(toName)},</p>" +
            $"<p>Your account application on RevolutionMedia.ai was not approved.</p>" +
            $"<p><strong>Reason:</strong> {HtmlEncode(reason)}</p>" +
            $"<p>If you believe this was a mistake, please contact the administrator.</p>");

    public Task<EmailResult> SendAccountSuspendedAsync(string toEmail, string toName, string reason, CancellationToken ct = default)
        => RenderOrFallback("account-suspended",
            "Your account has been suspended — RevolutionMedia.ai",
            new Dictionary<string, string?>
            {
                ["UserEmail"]    = toEmail,
                ["UserFullName"] = toName,
                ["Reason"]       = reason,
                ["LogoUrl"]      = _logoUrl,
            },
            $"<p>Hello {HtmlEncode(toName)},</p>" +
            $"<p>Your account on RevolutionMedia.ai has been suspended.</p>" +
            $"<p><strong>Reason:</strong> {HtmlEncode(reason)}</p>" +
            $"<p>Contact the administrator for more information.</p>");

    public Task<EmailResult> SendPlatformAccessGrantedAsync(
        string toEmail,
        string toName,
        string roleName,
        string platformName,
        CancellationToken ct = default)
        => RenderOrFallback("platform-access-granted",
            $"Your access to {platformName} is now active — RevolutionMedia.ai",
            new Dictionary<string, string?>
            {
                ["UserEmail"]    = toEmail,
                ["UserFullName"] = toName,
                ["RoleName"]     = roleName,
                ["PlatformName"] = platformName,
                ["LoginUrl"]     = _loginUrl,
                ["LogoUrl"]      = _logoUrl,
            },
            $"<p>Hello {HtmlEncode(toName)},</p>" +
            $"<p>You have been granted <strong>{HtmlEncode(roleName)}</strong> access to " +
            $"<strong>{HtmlEncode(platformName)}</strong> on RevolutionMedia.ai.</p>" +
            $"<p>Sign in with your @revolutionmedia.ai Google account: " +
            $"<a href=\"{_loginUrl}\">{_loginUrl}</a></p>");

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
