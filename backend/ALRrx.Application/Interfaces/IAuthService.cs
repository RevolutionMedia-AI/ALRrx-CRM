using ALRrx.Domain.Entities;

namespace ALRrx.Application.Interfaces;

public interface IAuthService
{
    string GenerateToken(AuthUser user);
    int? ValidateToken(string token);
    string GenerateVicidialFormToken(string user, string name, TimeSpan lifetime);
    Dictionary<string, string>? ValidateVicidialFormToken(string token);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}
