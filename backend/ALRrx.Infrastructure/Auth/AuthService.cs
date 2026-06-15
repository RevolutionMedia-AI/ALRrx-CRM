using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ALRrx.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IConfiguration _config;

    public AuthService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(AuthUser user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super-secret-key-alrrx-2026-min-32-chars!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.RoleName),
            new("status", user.Status.ToString()),
            new("platform_access", user.PlatformAccess.ToString()),
        };

        // Add permissions as a single space-delimited claim
        if (user.Permissions.Count > 0)
        {
            claims.Add(new Claim("permissions", string.Join(' ', user.Permissions)));
        }

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ALRrx",
            audience: _config["Jwt:Audience"] ?? "ALRrx",
            claims: claims,
            // Access token lifetime: 1 hour. The frontend proactively
            // refreshes via POST /api/auth/refresh when < 60s remain, so
            // an active user has a rolling session. An inactive user is
            // signed out after 1h and must log in again.
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int? ValidateToken(string token)
    {
        try
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"] ?? "super-secret-key-alrrx-2026-min-32-chars!"));

            var handler = new JwtSecurityTokenHandler();
            var result = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = _config["Jwt:Issuer"] ?? "ALRrx",
                ValidateAudience = true,
                ValidAudience = _config["Jwt:Audience"] ?? "ALRrx",
                ValidateLifetime = true,
                // 30s tolerance for clock drift between client and server.
                // The frontend proactively refreshes 60s before expiry, so
                // this skew window is a backstop for when the proactive
                // refresh fails (network blip, race condition).
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var id = result.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return id is not null ? int.Parse(id) : null;
        }
        catch
        {
            return null;
        }
    }

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
    public bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public IReadOnlyList<string> GetBootstrapAdminEmails()
    {
        var section = _config.GetSection("Auth:AdminBootstrapEmails");
        return section.Get<string[]>() ?? Array.Empty<string>();
    }
}
