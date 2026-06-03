using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class ValidateVicidialFormTokenUseCase
{
    private readonly IAuthService _auth;
    private readonly ILogger<ValidateVicidialFormTokenUseCase> _logger;

    public ValidateVicidialFormTokenUseCase(IAuthService auth, ILogger<ValidateVicidialFormTokenUseCase> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    public VicidialFormIdentity Execute(VicidialFormAuthRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
            throw new UnauthorizedAccessException("Token is required");

        var claims = _auth.ValidateVicidialFormToken(request.Token.Trim());
        if (claims is null)
        {
            _logger.LogWarning("Invalid or expired Vicidial form token");
            throw new UnauthorizedAccessException("Invalid or expired token");
        }

        return new VicidialFormIdentity
        {
            User = claims["user"],
            Name = claims["name"],
        };
    }
}
