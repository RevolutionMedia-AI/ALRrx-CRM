namespace ALRrx.Application.Interfaces;

public interface IRevokedUserStore
{
    void Revoke(int userId, int revokedBy, string reason);
    bool IsRevoked(int userId);
    (int RevokedBy, string Reason, DateTime RevokedAt)? GetRevocationInfo(int userId);
    void Clear(int userId);
}
