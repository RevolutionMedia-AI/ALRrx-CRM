using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// Thread-safe, in-memory user store keyed by email (case-insensitive).
/// All data is lost on restart — intended for prototyping until a DB layer is added.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, SliceUser> _byEmail =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public SliceUser? FindByEmail(string email)
    {
        _byEmail.TryGetValue(email, out var user);
        return user;
    }

    /// <inheritdoc/>
    public bool Add(SliceUser user) => _byEmail.TryAdd(user.Email, user);

    /// <inheritdoc/>
    public IReadOnlyList<SliceUser> GetAll() => [.. _byEmail.Values];
}
