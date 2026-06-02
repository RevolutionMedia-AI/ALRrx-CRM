using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace ALRrx.Application.UseCases;

public sealed class AuthenticateVicidialFormUseCase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthenticateVicidialFormUseCase> _logger;

    public AuthenticateVicidialFormUseCase(IConfiguration config, ILogger<AuthenticateVicidialFormUseCase> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<VicidialAuthResponse?> ExecuteAsync(VicidialAuthRequest request, CancellationToken ct = default)
    {
        var expectedKey = _config["VicidialForm:SharedKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            _logger.LogError("VicidialForm:SharedKey is not configured");
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.Key) || request.Key != expectedKey)
        {
            _logger.LogWarning("Invalid Vicidial form key attempt");
            return null;
        }

        var jwtKey = _config["Jwt:Key"] ?? "super-secret-key-alrrx-2026-min-32-chars!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddHours(8);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "vicidial-form"),
            new Claim(ClaimTypes.Email, "vicidial-form@system"),
            new Claim(ClaimTypes.Name, "Vicidial Form"),
            new Claim(ClaimTypes.Role, "VicidialForm"),
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "ALRrx",
            audience: _config["Jwt:Audience"] ?? "ALRrx",
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        var tokenString = await Task.FromResult(new JwtSecurityTokenHandler().WriteToken(token));
        _logger.LogInformation("Vicidial form authenticated; token expires at {ExpiresAt}", expiresAt);

        return new VicidialAuthResponse
        {
            Token = tokenString,
            ExpiresAt = expiresAt,
            FormName = "ALTRX Sales Form",
        };
    }
}
