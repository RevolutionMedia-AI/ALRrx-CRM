using System.Security.Claims;
using Slice.Api.Auth;

namespace Slice.Api.Middleware;

/// <summary>
/// Rejects authenticated requests from emails not in the Slice allowlist.
/// Applied after the authentication middleware so the email claim is already populated.
/// Uses the shared EmailAllowList singleton (also used by AuthController), so the
/// allowlist can be mutated at runtime via the InternalController without restarting.
/// </summary>
public sealed class SliceEmailGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly EmailAllowList _allowList;

    public SliceEmailGuardMiddleware(RequestDelegate next, EmailAllowList allowList)
    {
        _next = next;
        _allowList = allowList;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var email = context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

            if (!_allowList.IsAllowed(email))
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
