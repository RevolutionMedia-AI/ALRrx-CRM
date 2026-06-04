using System.Security.Claims;

namespace Slice.Api.Middleware;

/// <summary>
/// Rejects authenticated requests from emails not in the Slice allowlist.
/// Applied after the authentication middleware so the email claim is already populated.
/// The allowlist is cached at construction time (middleware instances are singletons in ASP.NET Core).
/// </summary>
public sealed class SliceEmailGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowedEmails;
    private readonly HashSet<string> _allowedDomains;

    public SliceEmailGuardMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _allowedEmails  = new HashSet<string>(
            config.GetSection("Slice:AllowedEmails").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
        _allowedDomains = new HashSet<string>(
            config.GetSection("Slice:AllowedDomains").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var email  = context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
            var domain = email.Contains('@') ? email.Split('@')[1] : string.Empty;

            bool isAllowed = _allowedEmails.Contains(email) || _allowedDomains.Contains(domain);

            if (!isAllowed)
            {
                context.Response.StatusCode  = 403;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"error\":\"Access restricted to authorized Slice accounts.\"}");
                return;
            }
        }

        await _next(context);
    }
}
