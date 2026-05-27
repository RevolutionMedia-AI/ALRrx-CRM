using System.Collections.Concurrent;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Repositories;

public sealed class InMemoryUserRepository
{
    private readonly ConcurrentDictionary<string, SliceUser> _byEmail = new(StringComparer.OrdinalIgnoreCase);

    public SliceUser? FindByEmail(string email)
    {
        _byEmail.TryGetValue(email, out var user);
        return user;
    }

    public bool Add(SliceUser user) => _byEmail.TryAdd(user.Email, user);

    public IReadOnlyList<SliceUser> GetAll() => [.. _byEmail.Values];
}
