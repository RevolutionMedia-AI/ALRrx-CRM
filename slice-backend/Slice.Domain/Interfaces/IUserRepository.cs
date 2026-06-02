using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

/// <summary>
/// Provides read/write access to the user store.
/// Implementations may be in-memory or database-backed without changing callers.
/// </summary>
public interface IUserRepository
{
    /// <summary>Returns the user matching <paramref name="email"/> (case-insensitive), or <c>null</c> if not found.</summary>
    SliceUser? FindByEmail(string email);

    /// <summary>
    /// Adds a user to the store.
    /// Returns <c>false</c> if a user with the same email already exists.
    /// </summary>
    bool Add(SliceUser user);

    /// <summary>Returns all users currently in the store.</summary>
    IReadOnlyList<SliceUser> GetAll();
}
