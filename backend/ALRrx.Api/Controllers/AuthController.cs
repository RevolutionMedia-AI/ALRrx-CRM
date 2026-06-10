using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IAuthService _auth;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthController(IUserRepository users, IAuthService auth, IConfiguration config, IWebHostEnvironment env)
    {
        _users = users;
        _auth = auth;
        _config = config;
        _env = env;
    }

    /// <summary>
    /// DEV ONLY — returns a real Admin JWT without credentials.
    /// Returns 404 in any environment that is not Development.
    /// </summary>
    [HttpPost("dev-login")]
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
            Role = Domain.Enums.UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
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
                Role = devUser.Role.ToString(),
                IsActive = devUser.IsActive,
                CreatedAt = devUser.CreatedAt
            }
        });
    }

    [HttpPost("google")]
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
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AuthController>>();
            logger.LogError(ex, "Google login failed");
            return Unauthorized(new { error = "Invalid Google credential" });
        }

        if (userInfo is null || string.IsNullOrEmpty(userInfo.Email))
            return Unauthorized(new { error = "Invalid Google token" });

        if (!userInfo.Email.EndsWith("@revolutionmedia.ai", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "Only @revolutionmedia.ai emails are allowed" });

        var (user, dbPersisted) = await TryUpsertUserAsync(userInfo, ct);

        if (user is { IsActive: false } && dbPersisted)
            return Unauthorized(new { error = "Account is deactivated" });

        if (!dbPersisted)
        {
            // DB is down/slow — best-effort persist in background so we don't
            // block the user. The Google token already proves identity + domain.
            _ = Task.Run(async () =>
            {
                using var bgCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var existing = await _users.GetByEmailAsync(userInfo.Email, bgCts.Token);
                    if (existing is null)
                    {
                        var newUser = new Domain.Entities.AuthUser
                        {
                            Email = userInfo.Email,
                            PasswordHash = string.Empty,
                            FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                            Role = Domain.Enums.UserRole.Admin,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        await _users.CreateAsync(newUser, bgCts.Token);
                    }
                }
                catch
                {
                    // best-effort — swallow
                }
            });
        }

        var token = _auth.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            User = new UserInfoDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            }
        });
    }

    private record GoogleUserInfo(string Email, string? Name);

    private async Task<(Domain.Entities.AuthUser user, bool dbPersisted)> TryUpsertUserAsync(
        GoogleUserInfo userInfo, CancellationToken ct)
    {
        using var dbCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        dbCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var user = await _users.GetByEmailAsync(userInfo.Email, dbCts.Token);
            if (user is null)
            {
                user = new Domain.Entities.AuthUser
                {
                    Email = userInfo.Email,
                    PasswordHash = string.Empty,
                    FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                    Role = Domain.Enums.UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _users.CreateAsync(user, dbCts.Token);
            }
            if (!user.IsActive)
                return (user, true); // propagate as-is; outer returns 401
            return (user, true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // DB is down/slow — synthesize from Google claims so login still works.
            return (new Domain.Entities.AuthUser
            {
                Id = 0,
                Email = userInfo.Email,
                PasswordHash = string.Empty,
                FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                Role = Domain.Enums.UserRole.Admin,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            }, false);
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken ct = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, ct);
        if (user is null || !_auth.VerifyPassword(request.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        if (!user.IsActive)
            return Unauthorized(new { error = "Account is deactivated" });

        var token = _auth.GenerateToken(user);

        return Ok(new LoginResponse
        {
            Token = token,
            User = new UserInfoDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            }
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("register")]
    public async Task<ActionResult<UserInfoDto>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken ct = default)
    {
        var existing = await _users.GetByEmailAsync(request.Email, ct);
        if (existing is not null)
            return Conflict(new { error = "Email already registered" });

        var adminId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var user = new Domain.Entities.AuthUser
        {
            Email = request.Email,
            PasswordHash = _auth.HashPassword(request.Password),
            FullName = request.FullName,
            Role = Enum.Parse<Domain.Enums.UserRole>(request.Role),
            IsActive = true,
            CreatedBy = adminId,
            CreatedAt = DateTime.UtcNow
        };

        await _users.CreateAsync(user, ct);

        return Ok(new UserInfoDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserInfoDto>> Me(CancellationToken ct = default)
    {
        var idClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var emailClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var nameClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // If we have claims, we can return immediately without DB lookup.
        if (!string.IsNullOrEmpty(idClaim) && int.TryParse(idClaim, out var id))
        {
            try
            {
                var user = await _users.GetByIdAsync(id, ct);
                if (user is not null)
                {
                    return Ok(new UserInfoDto
                    {
                        Id = user.Id,
                        Email = user.Email,
                        FullName = user.FullName,
                        Role = user.Role.ToString(),
                        IsActive = user.IsActive,
                        CreatedAt = user.CreatedAt
                    });
                }
            }
            catch
            {
                // Fall through to claim-based response
            }

            // DB miss but we still have claims — synthesize response from claims
            return Ok(new UserInfoDto
            {
                Id = id,
                Email = emailClaim ?? string.Empty,
                FullName = nameClaim ?? string.Empty,
                Role = roleClaim ?? "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        // No parseable id claim at all — just return from claims
        return Ok(new UserInfoDto
        {
            Id = 0,
            Email = emailClaim ?? string.Empty,
            FullName = nameClaim ?? string.Empty,
            Role = roleClaim ?? "Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
