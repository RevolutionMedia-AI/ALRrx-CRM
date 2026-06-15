using ALRrx.Domain.Entities;
using ALRrx.Domain.Enums;

namespace ALRrx.Application.Interfaces;

public interface IUserRepository
{
    Task<AuthUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<AuthUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<AuthUser>> GetAllAsync(CancellationToken ct = default);
    Task CreateAsync(AuthUser user, CancellationToken ct = default);
    Task UpdateAsync(AuthUser user, CancellationToken ct = default);
    Task EnsureAdminSeededAsync(CancellationToken ct = default);
    Task SetStatusAsync(int userId, UserStatus status, int? approvedBy, string? rejectionReason, CancellationToken ct = default);
    Task SetRoleAsync(int userId, int roleId, CancellationToken ct = default);
    Task RecordLoginAsync(int userId, bool success, CancellationToken ct = default);
    Task ResetPasswordAsync(int userId, string newHash, CancellationToken ct = default);
    Task<List<AuthUser>> GetByStatusAsync(UserStatus status, CancellationToken ct = default);
}
