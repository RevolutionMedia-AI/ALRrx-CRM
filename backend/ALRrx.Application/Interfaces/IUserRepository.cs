using ALRrx.Domain.Entities;

namespace ALRrx.Application.Interfaces;

public interface IUserRepository
{
    Task<AuthUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<AuthUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<AuthUser>> GetAllAsync(CancellationToken ct = default);
    Task CreateAsync(AuthUser user, CancellationToken ct = default);
    Task UpdateAsync(AuthUser user, CancellationToken ct = default);
    Task EnsureAdminSeededAsync(CancellationToken ct = default);
}
