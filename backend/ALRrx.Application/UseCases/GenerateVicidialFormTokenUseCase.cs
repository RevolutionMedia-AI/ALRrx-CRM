using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class GenerateVicidialFormTokenUseCase
{
    private readonly IVicidialUserLookup _users;
    private readonly IAuthService _auth;
    private readonly ILogger<GenerateVicidialFormTokenUseCase> _logger;

    public GenerateVicidialFormTokenUseCase(
        IVicidialUserLookup users,
        IAuthService auth,
        ILogger<GenerateVicidialFormTokenUseCase> logger)
    {
        _users = users;
        _auth = auth;
        _logger = logger;
    }

    public async Task<VicidialFormTokenResponse> ExecuteAsync(VicidialFormTokenRequest request, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.User))
            throw new ArgumentException("User is required");

        var userId = request.User.Trim();
        var info = await _users.GetActiveAltrxUserAsync(userId, ct);
        if (info is null)
            throw new ArgumentException($"Vicidial user '{userId}' is not active in ALTRX group");

        var lifetime = TimeSpan.FromHours(12);
        var token = _auth.GenerateVicidialFormToken(info.User, info.FullName, lifetime);
        var expiresAt = DateTime.UtcNow.Add(lifetime);

        _logger.LogInformation("Generated Vicidial form token for user={User}, name={Name}, expiresAt={ExpiresAt:o}", info.User, info.FullName, expiresAt);

        return new VicidialFormTokenResponse
        {
            Token = token,
            User = info.User,
            Name = info.FullName,
            ExpiresAt = expiresAt,
            Url = $"/form_sale?token={token}",
        };
    }
}
