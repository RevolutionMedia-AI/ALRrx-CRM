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
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.AccessToken);

            var userInfo = await http.GetFromJsonAsync<GoogleUserInfo>(
                "https://www.googleapis.com/oauth2/v3/userinfo", ct);

            if (userInfo is null || string.IsNullOrEmpty(userInfo.Email))
                return Unauthorized(new { error = "Invalid Google token" });

            if (!userInfo.Email.EndsWith("@revolutionmedia.ai", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { error = "Only @revolutionmedia.ai emails are allowed" });

            Domain.Entities.AuthUser user = null!;
            try
            {
                user = (await _users.GetByEmailAsync(userInfo.Email, ct))!;
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
                    await _users.CreateAsync(user, ct);
                }
                if (!user.IsActive)
                    return Unauthorized(new { error = "Account is deactivated" });
            }
            catch (MySqlConnector.MySqlException)
            {
                user = new Domain.Entities.AuthUser
                {
                    Id = 1,
                    Email = userInfo.Email,
                    FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                    Role = Domain.Enums.UserRole.Admin,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
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
        catch (Exception ex)
        {
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<AuthController>>();
            logger.LogError(ex, "Google login failed");
            return Unauthorized(new { error = "Invalid Google credential" });
        }
    }

    private record GoogleUserInfo(string Email, string? Name);

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
        var id = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return NotFound();

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
