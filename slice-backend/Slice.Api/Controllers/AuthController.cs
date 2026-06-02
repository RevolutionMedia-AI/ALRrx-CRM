using System.Net.Http.Headers;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Api.Controllers;

/// <summary>
/// Handles user authentication via Google OAuth and email/password,
/// plus user registration and profile retrieval.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly ILogger<AuthController> _logger;

    // Cached at construction time to avoid re-parsing config on every request.
    private readonly HashSet<string> _allowedEmails;
    private readonly HashSet<string> _allowedDomains;
    private readonly UserConfig[] _userConfigs;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        IValidator<LoginRequest> loginValidator,
        IValidator<RegisterRequest> registerValidator,
        ILogger<AuthController> logger)
    {
        _auth = auth;
        _users = users;
        _httpClientFactory = httpClientFactory;
        _loginValidator = loginValidator;
        _registerValidator = registerValidator;
        _logger = logger;

        _allowedEmails = new HashSet<string>(
            config.GetSection("Slice:AllowedEmails").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
        _allowedDomains = new HashSet<string>(
            config.GetSection("Slice:AllowedDomains").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
        _userConfigs = config.GetSection("Slice:Users").Get<UserConfig[]>() ?? [];
    }

    /// <summary>
    /// Autenticación con Google OAuth.
    /// El cliente envía su access_token de Google; el servidor lo valida contra
    /// la API de userinfo de Google y emite un JWT propio si el email está autorizado.
    /// </summary>
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return BadRequest(new { error = "access_token is required." });

        GoogleUserInfo? userInfo;
        try
        {
            var client = _httpClientFactory.CreateClient("Google");
            using var googleRequest = new HttpRequestMessage(HttpMethod.Get, "userinfo");
            googleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);
            using var googleResponse = await client.SendAsync(googleRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!googleResponse.IsSuccessStatusCode)
                return Unauthorized(new { error = "Invalid Google token." });

            userInfo = await googleResponse.Content.ReadFromJsonAsync<GoogleUserInfo>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Google userinfo");
            return Unauthorized(new { error = "Invalid Google token." });
        }

        if (userInfo is null || string.IsNullOrEmpty(userInfo.Email))
            return Unauthorized(new { error = "Invalid Google token." });

        if (!IsEmailAllowed(userInfo.Email))
        {
            _logger.LogWarning("Unauthorized Google login attempt: {Email}", userInfo.Email);
            return StatusCode(403, new { error = "This Google account is not authorized to access Slice." });
        }

        // Find or auto-provision the user (Google users don't need a password).
        var user = _users.FindByEmail(userInfo.Email);
        if (user == null)
        {
            user = new SliceUser
            {
                Email = userInfo.Email.ToLowerInvariant(),
                FullName = userInfo.Name ?? userInfo.Email.Split('@')[0],
                Role = ResolveRole(userInfo.Email),
                PasswordHash = string.Empty,
                IsActive = true,
            };
            _users.Add(user);
            _logger.LogInformation("Auto-provisioned user {Email} via Google OAuth", user.Email);
        }

        if (!user.IsActive)
            return StatusCode(403, new { error = "Account is deactivated." });

        var token = _auth.GenerateJwt(user);
        return Ok(new LoginResponse(token, user.Email, user.FullName, user.Role, DateTime.UtcNow.AddHours(8)));
    }

    /// <summary>
    /// Login con email y contraseña (para usuarios sin Google OAuth).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var validation = await _loginValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        if (!IsEmailAllowed(request.Email))
            return StatusCode(403, new { error = "This email is not authorized to access Slice." });

        var user = _users.FindByEmail(request.Email);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !_auth.VerifyPassword(request.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials." });

        if (!user.IsActive)
            return StatusCode(403, new { error = "Account is deactivated." });

        var token = _auth.GenerateJwt(user);
        return Ok(new LoginResponse(token, user.Email, user.FullName, user.Role, DateTime.UtcNow.AddHours(8)));
    }

    /// <summary>
    /// Registrar un nuevo usuario en la whitelist. Solo Admin.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var validation = await _registerValidator.ValidateAsync(request);
        if (!validation.IsValid)
            return BadRequest(new { errors = validation.Errors.Select(e => e.ErrorMessage) });

        if (!IsEmailAllowed(request.Email))
            return BadRequest(new { error = "Email is not in the Slice allowlist." });

        if (_users.FindByEmail(request.Email) != null)
            return Conflict(new { error = "Email already registered." });

        var user = new SliceUser
        {
            Email = request.Email.ToLowerInvariant(),
            FullName = request.FullName,
            Role = request.Role,
            PasswordHash = _auth.HashPassword(request.Password),
        };

        _users.Add(user);
        return Created(string.Empty, new UserInfoDto(user.Id, user.Email, user.FullName, user.Role, user.CreatedAt));
    }

    /// <summary>
    /// Retorna el perfil del usuario autenticado actualmente.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;
        var user = _users.FindByEmail(email);
        if (user == null) return NotFound();
        return Ok(new UserInfoDto(user.Id, user.Email, user.FullName, user.Role, user.CreatedAt));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private bool IsEmailAllowed(string email)
    {
        if (_allowedEmails.Contains(email)) return true;
        var domain = email.Contains('@') ? email.Split('@')[1] : string.Empty;
        return _allowedDomains.Contains(domain);
    }

    private string ResolveRole(string email)
    {
        var match = _userConfigs.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return match?.Role ?? "Viewer";
    }

    private record GoogleUserInfo(string Email, string? Name);
    private record UserConfig(string Email, string FullName, string Role);
}
