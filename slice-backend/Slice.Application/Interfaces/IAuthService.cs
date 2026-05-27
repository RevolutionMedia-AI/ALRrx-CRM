using Slice.Domain.Entities;

namespace Slice.Application.Interfaces;

public interface IAuthService
{
    string HashPassword(string plainPassword);
    bool VerifyPassword(string plainPassword, string hash);
    string GenerateJwt(SliceUser user);
}
