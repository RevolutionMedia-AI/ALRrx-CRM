using System.Collections.Concurrent;
using ALRrx.Application.Interfaces;

namespace ALRrx.Infrastructure.Auth;

public sealed class InMemoryRevokedUserStore : IRevokedUserStore
{
    public sealed record RevocationInfo(int RevokedBy, string Reason, DateTime RevokedAt);

    private readonly ConcurrentDictionary<int, RevocationInfo> _revoked = new();

    public void Revoke(int userId, int revokedBy, string reason)
    {
        _revoked[userId] = new RevocationInfo(revokedBy, reason, DateTime.UtcNow);
    }

    public bool IsRevoked(int userId) => _revoked.ContainsKey(userId);

    public (int RevokedBy, string Reason, DateTime RevokedAt)? GetRevocationInfo(int userId)
    {
        if (!_revoked.TryGetValue(userId, out var info)) return null;
        return (info.RevokedBy, info.Reason, info.RevokedAt);
    }

    public void Clear(int userId) => _revoked.TryRemove(userId, out _);
}
