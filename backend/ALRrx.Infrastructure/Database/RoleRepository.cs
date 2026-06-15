using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class RoleRepository : IRoleRepository
{
    private readonly CrmDbConnectionFactory _dbConnection;

    public RoleRepository(CrmDbConnectionFactory dbConnection)
    {
        _dbConnection = dbConnection;
    }

    private async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        return (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
    }

    public async Task<Role?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("SELECT Id, Name, Description, IsSystem, CreatedAt FROM alrrx_roles WHERE Id = @Id", connection);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapRole(reader);
    }

    public async Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("SELECT Id, Name, Description, IsSystem, CreatedAt FROM alrrx_roles WHERE Name = @Name", connection);
        cmd.Parameters.AddWithValue("@Name", name);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return MapRole(reader);
    }

    public async Task<List<Role>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("SELECT Id, Name, Description, IsSystem, CreatedAt FROM alrrx_roles ORDER BY Id", connection);
        var roles = new List<Role>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            roles.Add(MapRole(reader));
        return roles;
    }

    public async Task<List<Role>> GetAllWithPermissionsAsync(CancellationToken ct = default)
    {
        var roles = await GetAllAsync(ct);
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("SELECT RoleId, KeyName FROM alrrx_role_permissions rp JOIN alrrx_permissions p ON p.Id = rp.PermissionId", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var roleId = reader.GetInt32("RoleId");
            var key = reader.GetString("KeyName");
            var role = roles.FirstOrDefault(r => r.Id == roleId);
            if (role is not null) role.Permissions.Add(key);
        }
        return roles;
    }

    public async Task<List<string>> GetPermissionsForRoleAsync(int roleId, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT p.KeyName
            FROM alrrx_role_permissions rp
            JOIN alrrx_permissions p ON p.Id = rp.PermissionId
            WHERE rp.RoleId = @RoleId
            """, connection);
        cmd.Parameters.AddWithValue("@RoleId", roleId);
        var perms = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            perms.Add(reader.GetString("KeyName"));
        return perms;
    }

    public async Task<List<string>> GetPermissionsForRolesAsync(IEnumerable<int> roleIds, CancellationToken ct = default)
    {
        var ids = roleIds.ToList();
        if (ids.Count == 0) return [];
        await using var connection = await GetOpenConnectionAsync(ct);
        var inList = string.Join(",", ids);
        await using var cmd = new MySqlCommand($"""
            SELECT DISTINCT p.KeyName
            FROM alrrx_role_permissions rp
            JOIN alrrx_permissions p ON p.Id = rp.PermissionId
            WHERE rp.RoleId IN ({inList})
            """, connection);
        var perms = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            perms.Add(reader.GetString("KeyName"));
        return perms;
    }

    private static Role MapRole(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt32("Id"),
        Name = reader.GetString("Name"),
        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString("Description"),
        IsSystem = reader.GetBoolean("IsSystem"),
        CreatedAt = reader.GetDateTime("CreatedAt")
    };
}
