using System.Security.Claims;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Enums;

namespace ALRrx.Api.Middleware;

public sealed class UserStatusMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserStatusMiddleware> _logger;
    private readonly IRevokedUserStore _revoked;

    private static readonly HashSet<string> AllowAnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/google",
        "/api/auth/dev-login",
        "/api/auth/register",
        "/api/vicidial-form",
    };

    private static readonly HashSet<string> PendingOnlyPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/me",
        "/api/auth/logout",
    };

    public UserStatusMiddleware(RequestDelegate next, ILogger<UserStatusMiddleware> logger, IRevokedUserStore revoked)
    {
        _next = next;
        _logger = logger;
        _revoked = revoked;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (AllowAnonymousPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            await _next(context);
            return;
        }

        // BUG-008: Immediate token revocation. If the user was suspended/rejected
        // by an admin, their JWT becomes invalid for all requests except the
        // very few where we still want them to be able to read their own state.
        if (_revoked.IsRevoked(userId))
        {
            var info = _revoked.GetRevocationInfo(userId);
            _logger.LogInformation("Blocked revoked user {UserId} from {Path} (revoked at {RevokedAt} by {RevokedBy})",
                userId, path, info?.RevokedAt, info?.RevokedBy);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = "Bearer error=\"token_revoked\"";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Your session has been revoked — please log in again",
                code = "TOKEN_REVOKED"
            });
            return;
        }

        var statusClaim = context.User.FindFirst("status")?.Value;
        if (string.IsNullOrEmpty(statusClaim))
        {
            await _next(context);
            return;
        }

        if (!Enum.TryParse<UserStatus>(statusClaim, out var status))
        {
            await _next(context);
            return;
        }

        if (status == UserStatus.Pending)
        {
            if (!PendingOnlyPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Blocked Pending user {Email} from {Path}", context.User.Identity?.Name, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Account pending approval",
                    code = "USER_PENDING"
                });
                return;
            }
        }

        if (status == UserStatus.Suspended || status == UserStatus.Rejected)
        {
            if (!PendingOnlyPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation("Blocked {Status} user {Email} from {Path}", status, context.User.Identity?.Name, path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = $"Account {statusClaim.ToLowerInvariant()}",
                    code = status == UserStatus.Suspended ? "USER_SUSPENDED" : "USER_REJECTED"
                });
                return;
            }
        }

        await _next(context);
    }
}
