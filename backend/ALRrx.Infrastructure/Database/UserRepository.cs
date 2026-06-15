using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using ALRrx.Domain.Enums;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class UserRepository : IUserRepository
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IDatabaseConnection dbConnection, ILogger<UserRepository> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    private async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        return (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
    }

    public async Task EnsureAdminSeededAsync(CancellationToken ct = default)
    {
        const string createTable = """
            CREATE TABLE IF NOT EXISTS alrrx_users (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Email VARCHAR(255) NOT NULL UNIQUE,
                PasswordHash VARCHAR(255) NOT NULL,
                FullName VARCHAR(255) NOT NULL,
                Role VARCHAR(50) NOT NULL DEFAULT 'Employee',
                IsActive TINYINT(1) NOT NULL DEFAULT 1,
                CreatedBy INT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (CreatedBy) REFERENCES alrrx_users(Id)
            )
            """;

        await using var connection = await GetOpenConnectionAsync(ct);
        await using var createCmd = new MySqlCommand(createTable, connection);
        await createCmd.ExecuteNonQueryAsync(ct);

        var admins = new (string Email, string Name, string Hash, string Role)[]
        {
            ("kevin.escalante@revolutionmedia.ai", "Kevin Escalante", BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin"),
            ("david@revolutionmedia.ai",          "David",           BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin"),
            ("cuauhtemoc@revolutionmedia.ai",     "Cuauhtemoc",      BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin"),
            ("jessica.duarte@revolutionmedia.ai", "Jessica Duarte",  BCrypt.Net.BCrypt.HashPassword("Super123!"),  "Supervisor"),
        };

        foreach (var (email, name, hash, role) in admins)
        {
            await using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM alrrx_users WHERE Email = @Email", connection);
            checkCmd.Parameters.AddWithValue("@Email", email);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;
            if (exists) continue;

            // Resolve role id from alrrx_roles
            int roleId;
            await using (var roleCmd = new MySqlCommand(
                "SELECT Id FROM alrrx_roles WHERE Name = @Name", connection))
            {
                roleCmd.Parameters.AddWithValue("@Name", role);
                var res = await roleCmd.ExecuteScalarAsync(ct);
                if (res is null)
                {
                    _logger.LogWarning("Role '{Role}' not found in alrrx_roles — skipping seed for {Email}", role, email);
                    continue;
                }
                roleId = Convert.ToInt32(res);
            }

            await using var insertCmd = new MySqlCommand("""
                INSERT INTO alrrx_users (Email, PasswordHash, FullName, Role, RoleId, IsActive, Status, PlatformAccess, ApprovedAt)
                VALUES (@Email, @PasswordHash, @FullName, @Role, @RoleId, 1, 'Active', @PlatformAccess, @ApprovedAt)
                """, connection);
            insertCmd.Parameters.AddWithValue("@Email", email);
            insertCmd.Parameters.AddWithValue("@PasswordHash", hash);
            insertCmd.Parameters.AddWithValue("@FullName", name);
            insertCmd.Parameters.AddWithValue("@Role", role);
            insertCmd.Parameters.AddWithValue("@RoleId", roleId);
            insertCmd.Parameters.AddWithValue("@PlatformAccess", email == "jessica.duarte@revolutionmedia.ai" ? "Altrx" : "Both");
            insertCmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow);
            await insertCmd.ExecuteNonQueryAsync(ct);
            _logger.LogInformation("Seeded user: {Email} as {Role}", email, role);
        }
    }

    public async Task<AuthUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.SelectUserByEmail, connection);
        cmd.Parameters.AddWithValue("@Email", email);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return await MapUserAsync(reader, connection, ct);
    }

    public async Task<AuthUser?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.SelectUserById, connection);
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return await MapUserAsync(reader, connection, ct);
    }

    public async Task<List<AuthUser>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.SelectAllUsers, connection);

        var users = new List<AuthUser>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var u = await MapUserAsync(reader, connection, ct);
            if (u is not null) users.Add(u);
        }
        return users;
    }

    public async Task<List<AuthUser>> GetByStatusAsync(UserStatus status, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.SelectUsersByStatus, connection);
        cmd.Parameters.AddWithValue("@Status", status.ToString());

        var users = new List<AuthUser>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var u = await MapUserAsync(reader, connection, ct);
            if (u is not null) users.Add(u);
        }
        return users;
    }

    public async Task CreateAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);

        // Resolve the role name so the legacy `Role` column stays in sync with `RoleId`.
        // The legacy column has a NOT NULL constraint and a DEFAULT, but we want it
        // populated explicitly to keep the two in agreement.
        string roleName;
        await using (var nameCmd = new MySqlCommand("SELECT Name FROM alrrx_roles WHERE Id = @Id", connection))
        {
            nameCmd.Parameters.AddWithValue("@Id", user.RoleId);
            var res = await nameCmd.ExecuteScalarAsync(ct);
            if (res is null) throw new InvalidOperationException($"Role {user.RoleId} not found");
            roleName = (string)res;
        }

        await using var cmd = new MySqlCommand(Queries.InsertUser, connection);
        cmd.Parameters.AddWithValue("@Email", user.Email);
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
        cmd.Parameters.AddWithValue("@Role", roleName);
        cmd.Parameters.AddWithValue("@Status", user.Status.ToString());
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
        cmd.Parameters.AddWithValue("@CreatedBy", (object?)user.CreatedBy ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.UpdateUser, connection);
        cmd.Parameters.AddWithValue("@Id", user.Id);
        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@RoleId", user.RoleId);
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetStatusAsync(int userId, UserStatus status, int? approvedBy, string? rejectionReason, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(Queries.UpdateUserStatus, connection);
        cmd.Parameters.AddWithValue("@Id", userId);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@ApprovedBy", (object?)approvedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@RejectionReason", (object?)rejectionReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsActive", status == UserStatus.Active ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetRoleAsync(int userId, int roleId, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        // Also keep the legacy Role VARCHAR column in sync for backward compat
        string roleName;
        await using (var nameCmd = new MySqlCommand("SELECT Name FROM alrrx_roles WHERE Id = @Id", connection))
        {
            nameCmd.Parameters.AddWithValue("@Id", roleId);
            var res = await nameCmd.ExecuteScalarAsync(ct);
            if (res is null) throw new InvalidOperationException($"Role {roleId} not found");
            roleName = (string)res;
        }

        await using var cmd = new MySqlCommand(Queries.UpdateUserRole, connection);
        cmd.Parameters.AddWithValue("@Id", userId);
        cmd.Parameters.AddWithValue("@RoleId", roleId);
        cmd.Parameters.AddWithValue("@Role", roleName);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetPlatformAccessAsync(int userId, PlatformAccess access, int performedBy, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "UPDATE alrrx_users SET PlatformAccess = @Access WHERE Id = @Id", connection);
        cmd.Parameters.AddWithValue("@Access", access.ToString());
        cmd.Parameters.AddWithValue("@Id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordLoginAsync(int userId, bool success, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        if (success)
        {
            await using var cmd = new MySqlCommand("""
                UPDATE alrrx_users
                SET LastLoginAt = @Now, FailedLoginAttempts = 0, LockedUntil = NULL
                WHERE Id = @Id
                """, connection);
            cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@Id", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var cmd = new MySqlCommand("""
                UPDATE alrrx_users
                SET FailedLoginAttempts = FailedLoginAttempts + 1,
                    LockedUntil = CASE WHEN FailedLoginAttempts + 1 >= 5
                                       THEN DATE_ADD(NOW(), INTERVAL 30 MINUTE)
                                       ELSE LockedUntil END
                WHERE Id = @Id
                """, connection);
            cmd.Parameters.AddWithValue("@Id", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task ResetPasswordAsync(int userId, string newHash, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "UPDATE alrrx_users SET PasswordHash = @Hash, FailedLoginAttempts = 0, LockedUntil = NULL WHERE Id = @Id",
            connection);
        cmd.Parameters.AddWithValue("@Hash", newHash);
        cmd.Parameters.AddWithValue("@Id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<AuthUser?> MapUserAsync(MySqlDataReader reader, MySqlConnection connection, CancellationToken ct)
    {
        var roleId = reader.GetInt32("RoleId");
        var roleName = reader.GetString("Role");
        var id = reader.GetInt32("Id");
        var status = Enum.Parse<UserStatus>(reader.GetString("Status"));

        var user = new AuthUser
        {
            Id = id,
            Email = reader.GetString("Email"),
            PasswordHash = reader.GetString("PasswordHash"),
            FullName = reader.GetString("FullName"),
            RoleId = roleId,
            RoleName = roleName,
            Status = status,
            PlatformAccess = Enum.Parse<PlatformAccess>(reader.GetString("PlatformAccess")),
            IsActive = reader.GetBoolean("IsActive"),
            ApprovedBy = reader.IsDBNull(reader.GetOrdinal("ApprovedBy")) ? null : reader.GetInt32("ApprovedBy"),
            ApprovedAt = reader.IsDBNull(reader.GetOrdinal("ApprovedAt")) ? null : reader.GetDateTime("ApprovedAt"),
            RejectionReason = reader.IsDBNull(reader.GetOrdinal("RejectionReason")) ? null : reader.GetString("RejectionReason"),
            LastLoginAt = reader.IsDBNull(reader.GetOrdinal("LastLoginAt")) ? null : reader.GetDateTime("LastLoginAt"),
            FailedLoginAttempts = reader.GetInt32("FailedLoginAttempts"),
            LockedUntil = reader.IsDBNull(reader.GetOrdinal("LockedUntil")) ? null : reader.GetDateTime("LockedUntil"),
            CreatedBy = reader.IsDBNull(reader.GetOrdinal("CreatedBy")) ? null : reader.GetInt32("CreatedBy"),
            CreatedAt = reader.GetDateTime("CreatedAt"),
        };

        // Load permissions for this role
        await using var permCmd = new MySqlCommand(Queries.SelectPermissionsByRoleId, connection);
        permCmd.Parameters.AddWithValue("@RoleId", roleId);
        var perms = new List<string>();
        await using (var permReader = await permCmd.ExecuteReaderAsync(ct))
        {
            while (await permReader.ReadAsync(ct))
                perms.Add(permReader.GetString("KeyName"));
        }
        return user with { Permissions = perms };
    }

    private static class Queries
    {
        public const string SelectUserBase = """
            SELECT u.Id, u.Email, u.PasswordHash, u.FullName, u.RoleId, r.Name AS Role,
                   u.Status, u.PlatformAccess, u.IsActive, u.ApprovedBy, u.ApprovedAt, u.RejectionReason,
                   u.LastLoginAt, u.FailedLoginAttempts, u.LockedUntil, u.CreatedBy, u.CreatedAt
            FROM alrrx_users u
            JOIN alrrx_roles r ON r.Id = u.RoleId
            """;

        public static readonly string SelectUserByEmail = SelectUserBase + " WHERE u.Email = @Email";
        public static readonly string SelectUserById   = SelectUserBase + " WHERE u.Id = @Id";
        public static readonly string SelectAllUsers    = SelectUserBase + " ORDER BY u.CreatedAt DESC";
        public static readonly string SelectUsersByStatus = SelectUserBase + " WHERE u.Status = @Status ORDER BY u.CreatedAt DESC";

        public const string SelectPermissionsByRoleId = """
            SELECT p.KeyName
            FROM alrrx_role_permissions rp
            JOIN alrrx_permissions p ON p.Id = rp.PermissionId
            WHERE rp.RoleId = @RoleId
            """;

        public const string InsertUser = """
            INSERT INTO alrrx_users (Email, PasswordHash, FullName, RoleId, Role, Status, IsActive, CreatedBy)
            VALUES (@Email, @PasswordHash, @FullName, @RoleId, @Role, @Status, @IsActive, @CreatedBy)
            """;

        public const string UpdateUser = """
            UPDATE alrrx_users
            SET FullName = @FullName, PasswordHash = @PasswordHash, RoleId = @RoleId, IsActive = @IsActive
            WHERE Id = @Id
            """;

        public const string UpdateUserStatus = """
            UPDATE alrrx_users
            SET Status = @Status,
                ApprovedBy = @ApprovedBy,
                ApprovedAt = @ApprovedAt,
                RejectionReason = @RejectionReason,
                IsActive = @IsActive
            WHERE Id = @Id
            """;

        public const string UpdateUserRole = """
            UPDATE alrrx_users
            SET RoleId = @RoleId, Role = @Role
            WHERE Id = @Id
            """;
    }
}
