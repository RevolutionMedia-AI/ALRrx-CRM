using ALRrx.Domain.Entities;

namespace ALRrx.Application.Interfaces;

public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Role?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<List<Role>> GetAllAsync(CancellationToken ct = default);
    Task<List<Role>> GetAllWithPermissionsAsync(CancellationToken ct = default);
    Task<List<string>> GetPermissionsForRoleAsync(int roleId, CancellationToken ct = default);
    Task<List<string>> GetPermissionsForRolesAsync(IEnumerable<int> roleIds, CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    Task LogAsync(UserAuditLog entry, CancellationToken ct = default);
    Task<List<UserAuditLog>> GetForUserAsync(int userId, int limit = 50, CancellationToken ct = default);
    Task<List<UserAuditLog>> GetRecentAsync(int limit = 100, CancellationToken ct = default);
}
