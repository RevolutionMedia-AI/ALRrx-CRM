using Slice.Domain.Entities;

namespace Slice.Application.Interfaces;

/// <summary>
/// Provides password hashing (BCrypt) and JWT generation for Slice users.
/// </summary>
public interface IAuthService
{
    /// <summary>Returns a BCrypt hash of <paramref name="plainPassword"/> using work-factor 12.</summary>
    string HashPassword(string plainPassword);

    /// <summary>
    /// Verifies <paramref name="plainPassword"/> against a stored BCrypt <paramref name="hash"/>.
    /// Returns <c>true</c> if they match.
    /// </summary>
    bool VerifyPassword(string plainPassword, string hash);

    /// <summary>Generates a signed JWT for <paramref name="user"/>, valid for 8 hours.</summary>
    string GenerateJwt(SliceUser user);
}
