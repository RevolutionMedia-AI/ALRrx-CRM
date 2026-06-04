using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Auth;

/// <summary>
/// Implements <see cref="IAuthService"/> using BCrypt for password hashing
/// and HS256-signed JWTs for session tokens.
/// JWT configuration (key, issuer, audience) is read from <c>appsettings.json → Jwt</c>.
/// </summary>
public sealed class JwtAuthService : IAuthService
{
    private readonly IConfiguration _config;

    public JwtAuthService(IConfiguration config) => _config = config;

    /// <inheritdoc/>
    public string HashPassword(string plainPassword) =>
        BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);

    /// <inheritdoc/>
    public bool VerifyPassword(string plainPassword, string hash) =>
        BCrypt.Net.BCrypt.Verify(plainPassword, hash);

    /// <inheritdoc/>
    public string GenerateJwt(SliceUser user)
    {
        var jwtSection = _config.GetSection("Jwt");
        var key    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var expiry = DateTime.UtcNow.AddHours(8);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.FullName),
            new Claim(ClaimTypes.Role,           user.Role),
        };

        var token = new JwtSecurityToken(
            issuer:             jwtSection["Issuer"],
            audience:           jwtSection["Audience"],
            claims:             claims,
            expires:            expiry,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
