using System.Security.Claims;

namespace Slice.Api.Middleware;

/// <summary>
/// Rejects authenticated requests from emails not in the Slice allowlist.
/// Applied after authentication so the email claim is available.
/// </summary>
public sealed class SliceEmailGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _config;

    public SliceEmailGuardMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _config = config;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only guard authenticated requests
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var allowed = _config.GetSection("Slice:AllowedEmails").Get<string[]>() ?? [];
            var allowedDomains = _config.GetSection("Slice:AllowedDomains").Get<string[]>() ?? [];

            var domain = email.Contains('@') ? email.Split('@')[1] : string.Empty;
            bool isAllowed = allowed.Any(a => a.Equals(email, StringComparison.OrdinalIgnoreCase))
                          || allowedDomains.Any(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                context.Response.StatusCode = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Access restricted to authorized Slice accounts.\"}");
                return;
            }
        }

        await _next(context);
    }
}
