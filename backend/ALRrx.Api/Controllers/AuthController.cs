using System.Security.Claims;
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
            PasswordHash = string.Empty,
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

        if (!userInfo.Email.EndsWith("@revolutionmedia.ai", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "Only @revolutionmedia.ai emails are allowed" });

        var (user, dbPersisted) = await TryUpsertUserAsync(userInfo, ct);

        if (user is null)
            return Unauthorized(new { error = "Login failed" });

        if (user.Status == UserStatus.Pending)
            return StatusCode(403, new { error = "Account pending approval", code = "USER_PENDING" });
        if (user.Status == UserStatus.Suspended)
            return StatusCode(403, new { error = "Account suspended", code = "USER_SUSPENDED" });
        if (user.Status == UserStatus.Rejected)
            return StatusCode(403, new { error = "Account rejected", code = "USER_REJECTED" });

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
                // Decide role and status
                var isBootstrap = IsBootstrapAdmin(userInfo.Email);
                var role = await _roles.GetByNameAsync(isBootstrap ? "Admin" : "Employee", dbCts.Token)
                           ?? throw new InvalidOperationException("Default role not found");

                user = new Domain.Entities.AuthUser
                {
                    Email = userInfo.Email,
                    PasswordHash = string.Empty,
                    FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                    RoleId = role.Id,
                    RoleName = role.Name,
                    Status = isBootstrap ? UserStatus.Active : UserStatus.Pending,
                    IsActive = isBootstrap,
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
        catch
        {
            // DB is down/slow — synthesize from Google claims so login still works
            // (we still need a sensible role/status so JWT is valid)
            var isBootstrap = IsBootstrapAdmin(userInfo.Email);
            var synthesized = new Domain.Entities.AuthUser
            {
                Id = 0,
                Email = userInfo.Email,
                PasswordHash = string.Empty,
                FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                RoleId = isBootstrap ? 1 : 0,
                RoleName = isBootstrap ? "Admin" : "Employee",
                Status = isBootstrap ? UserStatus.Active : UserStatus.Pending,
                IsActive = isBootstrap,
                CreatedAt = DateTime.UtcNow,
            };
            return (synthesized, false);
        }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null || string.IsNullOrEmpty(user.PasswordHash) ||
            !_auth.VerifyPassword(request.Password, user.PasswordHash))
        {
            if (user is not null)
                await _users.RecordLoginAsync(user.Id, success: false, ct);
            return Unauthorized(new { error = "Invalid email or password" });
        }

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            return StatusCode(423, new { error = "Account temporarily locked. Try again later.", code = "USER_LOCKED" });

        if (user.Status == UserStatus.Pending)
            return StatusCode(403, new { error = "Account pending approval", code = "USER_PENDING" });
        if (user.Status == UserStatus.Suspended)
            return StatusCode(403, new { error = "Account suspended", code = "USER_SUSPENDED" });
        if (user.Status == UserStatus.Rejected)
            return StatusCode(403, new { error = "Account rejected", code = "USER_REJECTED" });

        if (!user.IsActive)
            return Unauthorized(new { error = "Account is deactivated" });

        await _users.RecordLoginAsync(user.Id, success: true, ct);
        await _audit.LogAsync(new Domain.Entities.UserAuditLog
        {
            UserId = user.Id,
            Action = "Login",
            IpAddress = ClientIp,
        }, ct);

        var token = _auth.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            User = MapUser(user),
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<UserInfoDto>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct = default)
    {
        var existing = await _users.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            return Conflict(new { error = "Email already registered" });

        var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var role = await _roles.GetByIdAsync(request.RoleId, ct);
        if (role is null) return BadRequest(new { error = "Invalid roleId" });

        var user = new Domain.Entities.AuthUser
        {
            Email = request.Email,
            PasswordHash = _auth.HashPassword(request.Password),
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
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct = default)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;
        var nameClaim = User.FindFirst(ClaimTypes.Name)?.Value;
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        var statusClaim = User.FindFirst("status")?.Value;
        var platformClaim = User.FindFirst("platform_access")?.Value;
        var permsClaim = User.FindFirst("permissions")?.Value;

        if (!string.IsNullOrEmpty(idClaim) && int.TryParse(idClaim, out var id))
        {
            try
            {
                var user = await _users.GetByIdAsync(id, ct);
                if (user is not null)
                    return Ok(MapUser(user));
            }
            catch
            {
                // Fall through
            }

            return Ok(new UserInfoDto
            {
                Id = id,
                Email = emailClaim ?? string.Empty,
                FullName = nameClaim ?? string.Empty,
                Role = roleClaim ?? "Admin",
                Status = statusClaim ?? "Active",
                PlatformAccess = platformClaim ?? "None",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Permissions = string.IsNullOrEmpty(permsClaim) ? [] : permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
            });
        }

        return Ok(new UserInfoDto
        {
            Id = 0,
            Email = emailClaim ?? string.Empty,
            FullName = nameClaim ?? string.Empty,
            Role = roleClaim ?? "Admin",
            Status = statusClaim ?? "Active",
            PlatformAccess = platformClaim ?? "None",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            Permissions = string.IsNullOrEmpty(permsClaim) ? [] : permsClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
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
    };
}
