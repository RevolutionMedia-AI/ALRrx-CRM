using System.Security.Claims;
using System.Globalization;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IAuthService _auth;
    private readonly IAuditLogRepository _audit;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository users,
        IRoleRepository roles,
        IAuthService auth,
        IAuditLogRepository audit,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<AuthController> logger)
    {
        _users = users;
        _roles = roles;
        _auth = auth;
        _audit = audit;
        _config = config;
        _env = env;
        _logger = logger;
    }

    private string? ClientIp =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ??
        Request.Headers["X-Forwarded-For"].FirstOrDefault();

    private bool IsBootstrapAdmin(string email) =>
        _auth.GetBootstrapAdminEmails().Contains(email, StringComparer.OrdinalIgnoreCase);

    [HttpPost("dev-login")]
    [EnableRateLimiting("auth")]
    public ActionResult<LoginResponse> DevLogin()
    {
        if (!_env.IsDevelopment())
            return NotFound();

        var devUser = new Domain.Entities.AuthUser
        {
            Id = 0,
            Email = "dev@local.test",
            FullName = "Dev (local)",
            RoleId = 1,
            RoleName = "Admin",
            Status = UserStatus.Active,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        };

        var token = _auth.GenerateToken(devUser);

        return Ok(new LoginResponse
        {
            Token = token,
            User = new UserInfoDto
            {
                Id = devUser.Id,
                Email = devUser.Email,
                FullName = devUser.FullName,
                RoleId = devUser.RoleId,
                Role = devUser.RoleName,
                Status = devUser.Status.ToString(),
                IsActive = devUser.IsActive,
                CreatedAt = devUser.CreatedAt,
            }
        });
    }

    [HttpPost("google")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> GoogleLogin(
        [FromBody] GoogleLoginRequest request,
        CancellationToken ct = default)
    {
        GoogleUserInfo? userInfo;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.AccessToken);

            userInfo = await http.GetFromJsonAsync<GoogleUserInfo>(
                "https://www.googleapis.com/oauth2/v3/userinfo", ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unauthorized(new { error = "Google verification timed out" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google login failed");
            return Unauthorized(new { error = "Invalid Google credential" });
        }

        if (userInfo is null || string.IsNullOrEmpty(userInfo.Email))
            return Unauthorized(new { error = "Invalid Google token" });

        // BUG-6 fix: trim whitespace before the domain check, otherwise
        // emails like "  user@revolutionmedia.ai  " fail the EndsWith test
        // and the user is wrongly told their domain is not allowed.
        var email = userInfo.Email.Trim();

        // BUG-7 fix: reject homograph attacks where the domain contains
        // non-ASCII (Cyrillic 'і', fullwidth '@', etc) lookalikes. A legit
        // @revolutionmedia.ai account will always be pure ASCII since
        // Google does not allow unicode in verified email addresses. We
        // also normalize via IdnMapping to defend against any future
        // punycode edge cases.
        if (!IsAsciiEmail(email) || !NormalizeEmailDomain(email).EndsWith("@revolutionmedia.ai", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "Only @revolutionmedia.ai emails are allowed" });
        userInfo = userInfo with { Email = email };

        var (user, dbPersisted) = await TryUpsertUserAsync(userInfo, ct);

        if (user is null)
        {
            // BUG-10 fix: TryUpsertUserAsync returns null when the user DB is
            // unreachable. We refuse to issue a JWT in that case (it would
            // carry Id=0 and break every downstream check), so we surface a
            // 503 to the client and let them retry.
            return StatusCode(503, new { error = "User service temporarily unavailable. Please try again." });
        }

        if (user.Status == UserStatus.Pending)
            return StatusCode(403, new { error = "Account pending approval", code = "USER_PENDING" });
        if (user.Status == UserStatus.Suspended)
            return StatusCode(403, new { error = "Account suspended", code = "USER_SUSPENDED" });
        if (user.Status == UserStatus.Rejected)
            return StatusCode(403, new { error = "Account rejected", code = "USER_REJECTED" });
        // BUG-9 fix: GoogleLogin now respects the same IsActive flag as Login.
        if (!user.IsActive)
            return Unauthorized(new { error = "Account is deactivated" });
        // BUG-8 fix: GoogleLogin now respects the same account lockout as Login.
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            return StatusCode(423, new { error = "Account temporarily locked. Try again later.", code = "USER_LOCKED" });

        if (dbPersisted)
        {
            await _users.RecordLoginAsync(user.Id, success: true, ct);
            await _audit.LogAsync(new Domain.Entities.UserAuditLog
            {
                UserId = user.Id,
                Action = "Login",
                IpAddress = ClientIp,
            }, ct);
        }

        var token = _auth.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            User = MapUser(user),
        });
    }

    private record GoogleUserInfo(string Email, string? Name);

    private async Task<(Domain.Entities.AuthUser? user, bool dbPersisted)> TryUpsertUserAsync(
        GoogleUserInfo userInfo, CancellationToken ct)
    {
        using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        dbCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var user = await _users.GetByEmailAsync(userInfo.Email, dbCts.Token);
            if (user is null)
            {
                // All @revolutionmedia.ai users can sign in. Bootstrap admins
                // become Admin/Active immediately. Everyone else is created
                // with the Pending role (0 permissions) and Active status so
                // they can sign in but the frontend sends them to /no-access
                // until an admin assigns them a real role.
                var isBootstrap = IsBootstrapAdmin(userInfo.Email);
                var roleName = isBootstrap ? "Admin" : "Pending";
                var role = await _roles.GetByNameAsync(roleName, dbCts.Token)
                           ?? throw new InvalidOperationException($"Default role '{roleName}' not found");

                user = new Domain.Entities.AuthUser
                {
                    Email = userInfo.Email,
                    FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                    RoleId = role.Id,
                    RoleName = role.Name,
                    Status = UserStatus.Active,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                };

                await _users.CreateAsync(user, dbCts.Token);

                await _audit.LogAsync(new Domain.Entities.UserAuditLog
                {
                    UserId = user.Id,
                    Action = "Registered",
                    NewValue = user.Status.ToString(),
                    IpAddress = ClientIp,
                }, dbCts.Token);
            }
            return (user, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // BUG-10 fix: do NOT synthesize a fake user with Id=0. That JWT
            // would carry NameIdentifier=0 and RoleId=0/1, which would break
            // every downstream operation that uses those values (audit log FKs,
            // user queries, role checks). Returning null here causes the
            // controller to respond 503, which the frontend will surface as
            // "service unavailable — try again" instead of giving the user a
            // broken session.
            _logger.LogError(ex, "User DB unavailable during Google login for {Email}", userInfo.Email);
            return (null, false);
        }
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<UserInfoDto>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct = default)
    {
        // Admin can pre-create a user. No password — the user must sign in
        // via Google. Until an admin assigns them a real role, they get
        // the Pending role (0 permissions) and are routed to /no-access
        // on first login.
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!IsAsciiEmail(email) ||
            !NormalizeEmailDomain(email).EndsWith("@revolutionmedia.ai", StringComparison.OrdinalIgnoreCase)) {
            return BadRequest(new { error = "Invalid email — must be a valid @revolutionmedia.ai address" });
        }

        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null)
            return Conflict(new { error = "Email already registered" });

        var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null) return BadRequest(new { error = "Invalid roleId" });

        var user = new Domain.Entities.AuthUser
        {
            Email = email,
            FullName = request.FullName,
            RoleId = role.Id,
            RoleName = role.Name,
            Status = UserStatus.Active,
            IsActive = true,
            CreatedBy = adminId,
            CreatedAt = DateTime.UtcNow,
        };

        await _users.CreateAsync(user, ct);

        await _audit.LogAsync(new Domain.Entities.UserAuditLog
        {
            UserId = user.Id,
            Action = "Registered",
            NewValue = user.Status.ToString(),
            IpAddress = ClientIp,
        }, ct);

        var created = await _users.GetByEmailAsync(user.Email, ct);
        return Ok(MapUser(created ?? user));
    }

    [Authorize]
    [HttpGet("me")]
    [EnableRateLimiting("authCheck")]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct = default)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;
        var nameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        var statusClaim = User.FindFirst("status")?.Value;
        var platformClaim = User.FindFirst("platform_access")?.Value;
        var permsClaim = User.FindFirst("permissions")?.Value;

        if (string.IsNullOrEmpty(idClaim) || !int.TryParse(idClaim, out var id))
        {
            // Token has no usable user id — treat as unauthenticated.
            return Unauthorized(new { error = "Invalid token" });
        }

        try
        {
            var user = await _users.GetByIdAsync(id, ct);
            if (user is not null)
                return Ok(MapUser(user));
        }
        catch
        {
            // BUG-11 fix: do NOT fall back to JWT-claim data when the DB is
            // down. A suspended/rejected user would otherwise still see
            // `status: Active` in the frontend (from the stale claim) and
            // try to navigate, only to be rejected by UserStatusMiddleware
            // with a 403. Returning 503 makes the frontend show "service
            // unavailable" and the user can retry once the DB recovers.
            _logger.LogWarning("User DB unavailable for /auth/me userId={Id}", id);
            return StatusCode(503, new { error = "User service temporarily unavailable. Please try again." });
        }

        // Token references a userId that no longer exists in the DB.
        return Unauthorized(new { error = "User no longer exists" });
    }

    // BUG-20 fix: explicit sign-out endpoint. JWTs are stateless so we
    // cannot truly invalidate the token server-side without a blacklist,
    // but acknowledging the logout gives us:
    //   - A place to log the action in the audit log.
    //   - A signal to the frontend that the backend is reachable, so the
    //     local token cleanup only happens after the round-trip succeeds.
    //   - Forward compatibility: if we add a token blacklist later, the
    //     frontend already calls the right endpoint.
    [Authorize]
    [HttpPost("logout")]
    [EnableRateLimiting("authCheck")]
    public async Task<IActionResult> Logout(CancellationToken ct = default) {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idClaim, out var userId)) {
            try {
                await _audit.LogAsync(new Domain.Entities.UserAuditLog {
                    UserId = userId,
                    Action = "Logout",
                    IpAddress = ClientIp,
                }, ct);
            } catch {
                // Best-effort. The user's local token is cleared regardless.
            }
        }
        return NoContent();
    }

    /// <summary>
    /// Issue a fresh JWT for an already-authenticated user. The current
    /// token must still be valid (not expired, signature OK). The new
    /// token replaces the old one in the client's storage; the old
    /// token is not blacklisted because JWTs are stateless and any
    /// further use of the old one would still pass the signature check
    /// — instead we rely on the short access-token lifetime to limit
    /// the window of opportunity for a stolen token.
    ///
    /// Re-validates the user's status from the DB so a suspended user
    /// who somehow still has a valid token cannot keep refreshing.
    /// </summary>
    [Authorize]
    [HttpPost("refresh")]
    [EnableRateLimiting("authCheck")]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken ct = default) {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idClaim, out var userId)) {
            return Unauthorized(new { error = "Invalid token" });
        }

        Domain.Entities.AuthUser? user;
        try {
            user = await _users.GetByIdAsync(userId, ct);
        } catch {
            // BUG-11 fix: same as /auth/me — if the DB is down, refuse
            // to mint a new token (we can't re-validate the user's
            // current status).
            return StatusCode(503, new { error = "User service temporarily unavailable. Please try again." });
        }

        if (user is null) {
            return Unauthorized(new { error = "User no longer exists" });
        }
        if (user.Status == UserStatus.Pending) {
            return StatusCode(403, new { error = "Account pending approval", code = "USER_PENDING" });
        }
        if (user.Status == UserStatus.Suspended) {
            return StatusCode(403, new { error = "Account suspended", code = "USER_SUSPENDED" });
        }
        if (user.Status == UserStatus.Rejected) {
            return StatusCode(403, new { error = "Account rejected", code = "USER_REJECTED" });
        }
        if (!user.IsActive) {
            return Unauthorized(new { error = "Account is deactivated" });
        }
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow) {
            return StatusCode(423, new { error = "Account temporarily locked. Try again later.", code = "USER_LOCKED" });
        }

        // Mint a fresh token. The new token is independent of the old
        // one — both will pass signature validation until the old one
        // expires. This is the trade-off we accept for stateless JWTs.
        var token = _auth.GenerateToken(user);
        return Ok(new LoginResponse {
            Token = token,
            User = MapUser(user),
        });
    }

    private static UserInfoDto MapUser(Domain.Entities.AuthUser u) => new()
    {
        Id = u.Id,
        Email = u.Email,
        FullName = u.FullName,
        RoleId = u.RoleId,
        Role = u.RoleName,
        Status = u.Status.ToString(),
        PlatformAccess = u.PlatformAccess.ToString(),
        IsActive = u.IsActive,
        LastLoginAt = u.LastLoginAt,
        CreatedAt = u.CreatedAt,
        Permissions = u.Permissions,
        HasAccess = u.Permissions.Count > 0,
    };

    // BUG-7 helpers
    private static bool IsAsciiEmail(string email) {
        for (var i = 0; i < email.Length; i++) {
            if (email[i] > 127) return false;
        }
        return true;
    }

    private static string NormalizeEmailDomain(string email) {
        var at = email.LastIndexOf('@');
        if (at < 0) return email;
        var local = email[..at];
        var domain = email[(at + 1)..];
        try {
            var idn = new System.Globalization.IdnMapping();
            domain = idn.GetAscii(domain);
        } catch {
            return email;
        }
        return local + "@" + domain;
    }
}
