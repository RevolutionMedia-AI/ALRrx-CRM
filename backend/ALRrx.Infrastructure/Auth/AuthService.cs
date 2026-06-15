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
            expires: DateTime.UtcNow.AddHours(12),
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
                ClockSkew = TimeSpan.Zero
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
