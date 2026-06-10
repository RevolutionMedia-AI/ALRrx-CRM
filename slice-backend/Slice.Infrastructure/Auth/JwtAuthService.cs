using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Auth;

/// <summary>
/// Implements <see cref="IAuthService"/> using BCrypt for password hashing
/// and HS256-signed JWTs for session tokens.
/// JWT configuration (key, issuer, audience) is read from
/// <c>appsettings.json → Jwt</c>. The signing key and validation parameters are
/// cached in <see cref="JwtKeyCache"/> to avoid re-encoding the key bytes and
/// rebuilding the <see cref="TokenValidationParameters"/> on every request.
/// </summary>
public sealed class JwtAuthService : IAuthService
{
    private readonly IConfiguration _config;
    private readonly JwtKeyCache _keyCache;
    private readonly Slice.Infrastructure.Diagnostics.SlicePerformanceMetrics _metrics;

    public JwtAuthService(
        IConfiguration config,
        JwtKeyCache keyCache,
        Slice.Infrastructure.Diagnostics.SlicePerformanceMetrics metrics)
    {
        _config    = config;
        _keyCache  = keyCache;
        _metrics   = metrics;
    }

    /// <inheritdoc/>
    public string HashPassword(string plainPassword) =>
        BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 11);

    /// <inheritdoc/>
    public bool VerifyPassword(string plainPassword, string hash) =>
        BCrypt.Net.BCrypt.Verify(plainPassword, hash);

    /// <inheritdoc/>
    public string GenerateJwt(SliceUser user)
    {
        var sw = Stopwatch.StartNew();
        var jwtSection = _config.GetSection("Jwt");
        var key    = _keyCache.GetSigningKey(jwtSection["Key"]!);
        var expiry = DateTime.UtcNow.AddHours(8);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.FullName),
            new Claim(ClaimTypes.Role,           user.Role),
        };

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer:             jwtSection["Issuer"],
            audience:           jwtSection["Audience"],
            claims:             claims,
            expires:            expiry,
            signingCredentials: creds);

        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        sw.Stop();
        _metrics.RecordJwtSign(sw.ElapsedTicks, cacheHit: true);
        return jwt;
    }
}
