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
        await using var connection = await GetOpenConnectionAsync(ct);

        // ============================================================================
        // 1. alrrx_roles — must come before alrrx_users because of FK
        // ============================================================================
        await ExecAsync(connection, """
            CREATE TABLE IF NOT EXISTS alrrx_roles (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Name VARCHAR(50) NOT NULL UNIQUE,
                Description VARCHAR(255) NULL,
                IsSystem TINYINT(1) NOT NULL DEFAULT 0,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        await ExecAsync(connection, """
            INSERT INTO alrrx_roles (Name, Description, IsSystem) VALUES
                ('Admin',          'Full system access',              1),
                ('Supervisor',     'Team management + read all',      1),
                ('Employee',       'Read-only basic access',          1),
                ('VicidialEditor', 'Can edit Vicidial sales entries', 1)
            ON DUPLICATE KEY UPDATE Description = VALUES(Description)
            """, ct);

        // ============================================================================
        // 2. alrrx_permissions
        // ============================================================================
        await ExecAsync(connection, """
            CREATE TABLE IF NOT EXISTS alrrx_permissions (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                KeyName VARCHAR(100) NOT NULL UNIQUE,
                Description VARCHAR(255) NULL,
                Module VARCHAR(50) NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        await ExecAsync(connection, """
            INSERT INTO alrrx_permissions (KeyName, Description, Module) VALUES
                ('users.view',              'View users list',                       'users'),
                ('users.approve',           'Approve pending users',                'users'),
                ('users.edit',              'Edit user details and roles',          'users'),
                ('users.suspend',           'Suspend/reactivate users',             'users'),
                ('admin.view',              'Access admin panel',                   'admin'),
                ('dashboard.view',          'View dashboards',                      'dashboard'),
                ('reports.view',            'View reports',                         'reports'),
                ('staffing.view',           'View staffing',                        'staffing'),
                ('staffing.view.team',      'View team staffing (Supervisor only)', 'staffing'),
                ('twilio.view',             'View Twilio costs',                    'twilio'),
                ('vicidial.view',           'View Vicidial sales',                  'vicidial'),
                ('vicidial.edit',           'Edit Vicidial sales',                  'vicidial'),
                ('period-comparison.run',   'Run period comparison exports',        'reports'),
                ('data.edit',               'Edit CRM data rows',                   'data'),
                ('data.delete',             'Delete CRM data rows',                 'data')
            ON DUPLICATE KEY UPDATE Description = VALUES(Description), Module = VALUES(Module)
            """, ct);

        // ============================================================================
        // 3. alrrx_role_permissions
        // ============================================================================
        await ExecAsync(connection, """
            CREATE TABLE IF NOT EXISTS alrrx_role_permissions (
                RoleId INT NOT NULL,
                PermissionId INT NOT NULL,
                PRIMARY KEY (RoleId, PermissionId),
                CONSTRAINT FK_rp_role FOREIGN KEY (RoleId) REFERENCES alrrx_roles(Id) ON DELETE CASCADE,
                CONSTRAINT FK_rp_perm FOREIGN KEY (PermissionId) REFERENCES alrrx_permissions(Id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        // Admin gets ALL permissions
        await ExecAsync(connection, """
            INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
            SELECT r.Id, p.Id FROM alrrx_roles r CROSS JOIN alrrx_permissions p WHERE r.Name = 'Admin'
            """, ct);

        // Supervisor: read all + manage team
        await ExecAsync(connection, """
            INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
            SELECT r.Id, p.Id
            FROM alrrx_roles r
            JOIN alrrx_permissions p ON p.KeyName IN (
                'users.view', 'dashboard.view', 'reports.view', 'staffing.view',
                'staffing.view.team', 'twilio.view', 'vicidial.view', 'period-comparison.run'
            )
            WHERE r.Name = 'Supervisor'
            """, ct);

        // Employee: basic read
        await ExecAsync(connection, """
            INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
            SELECT r.Id, p.Id
            FROM alrrx_roles r
            JOIN alrrx_permissions p ON p.KeyName IN (
                'dashboard.view', 'reports.view', 'vicidial.view'
            )
            WHERE r.Name = 'Employee'
            """, ct);

        // VicidialEditor: vicidial only
        await ExecAsync(connection, """
            INSERT IGNORE INTO alrrx_role_permissions (RoleId, PermissionId)
            SELECT r.Id, p.Id
            FROM alrrx_roles r
            JOIN alrrx_permissions p ON p.KeyName IN ('vicidial.view', 'vicidial.edit')
            WHERE r.Name = 'VicidialEditor'
            """, ct);

        // ============================================================================
        // 4. alrrx_user_audit_log
        // ============================================================================
        await ExecAsync(connection, """
            CREATE TABLE IF NOT EXISTS alrrx_user_audit_log (
                Id BIGINT AUTO_INCREMENT PRIMARY KEY,
                UserId INT NOT NULL,
                Action VARCHAR(50) NOT NULL,
                PerformedBy INT NULL,
                OldValue VARCHAR(255) NULL,
                NewValue VARCHAR(255) NULL,
                Reason VARCHAR(500) NULL,
                IpAddress VARCHAR(45) NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX IX_audit_user_created (UserId, CreatedAt),
                INDEX IX_audit_action (Action)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        // ============================================================================
        // 5. alrrx_users — full new schema
        // ============================================================================
        await ExecAsync(connection, """
            CREATE TABLE IF NOT EXISTS alrrx_users (
                Id INT AUTO_INCREMENT PRIMARY KEY,
                Email VARCHAR(255) NOT NULL UNIQUE,
                PasswordHash VARCHAR(255) NOT NULL,
                FullName VARCHAR(255) NOT NULL,
                Role VARCHAR(50) NOT NULL DEFAULT 'Employee',
                RoleId INT NULL,
                Status ENUM('Pending','Active','Rejected','Suspended') NOT NULL DEFAULT 'Active',
                PlatformAccess ENUM('None','Altrx','Slice','Both') NOT NULL DEFAULT 'None',
                IsActive TINYINT(1) NOT NULL DEFAULT 1,
                ApprovedBy INT NULL,
                ApprovedAt DATETIME NULL,
                RejectionReason VARCHAR(500) NULL,
                LastLoginAt DATETIME NULL,
                FailedLoginAttempts INT NOT NULL DEFAULT 0,
                LockedUntil DATETIME NULL,
                CreatedBy INT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                INDEX IX_alrrx_users_Status (Status),
                INDEX IX_alrrx_users_PlatformAccess (PlatformAccess)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        // ============================================================================
        // 6. Add new columns to pre-existing alrrx_users (legacy schema)
        //    Try/catch on error 1060 (Duplicate column) — already applied
        // ============================================================================
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN RoleId INT NULL", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN Status ENUM('Pending','Active','Rejected','Suspended') NOT NULL DEFAULT 'Active'", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN PlatformAccess ENUM('None','Altrx','Slice','Both') NOT NULL DEFAULT 'None'", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN ApprovedBy INT NULL", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN ApprovedAt DATETIME NULL", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN RejectionReason VARCHAR(500) NULL", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN LastLoginAt DATETIME NULL", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN FailedLoginAttempts INT NOT NULL DEFAULT 0", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD COLUMN LockedUntil DATETIME NULL", ct);
        await TryAlterAsync(connection,
            "CREATE INDEX IX_alrrx_users_Status ON alrrx_users(Status)", ct);
        await TryAlterAsync(connection,
            "CREATE INDEX IX_alrrx_users_PlatformAccess ON alrrx_users(PlatformAccess)", ct);

        // ============================================================================
        // 7. Migrate existing data — fill new columns from legacy values
        // ============================================================================
        // Set RoleId from legacy Role varchar
        await ExecAsync(connection, """
            UPDATE alrrx_users u
            JOIN alrrx_roles r ON r.Name = u.Role
            SET u.RoleId = r.Id
            WHERE u.RoleId IS NULL
            """, ct);

        // Auto-approve existing users based on IsActive
        await ExecAsync(connection, """
            UPDATE alrrx_users
            SET Status = CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Suspended' END,
                ApprovedAt = COALESCE(ApprovedAt, CreatedAt)
            WHERE (Status = 'Active' AND IsActive = 0)
               OR (ApprovedAt IS NULL AND IsActive = 1)
            """, ct);

        // Migrate PlatformAccess from the legacy hardcoded email list
        await ExecAsync(connection, """
            UPDATE alrrx_users
            SET PlatformAccess = CASE
                WHEN LOWER(Email) IN (
                    'david@revolutionmedia.ai', 'j.lines@revolutionmedia.ai',
                    'cuauhtemoc@revolutionmedia.ai', 'kevin.escalante@revolutionmedia.ai'
                ) THEN 'Both'
                WHEN LOWER(Email) IN (
                    'jessica.duarte@revolutionmedia.ai', 'silverio.arellano@revolutionmedia.ai'
                ) THEN 'Altrx'
                WHEN LOWER(Email) IN (
                    'pedro@revolutionmedia.ai', 'ofelia.palomino@revolutionmedia.ai',
                    'victor.ramirez@revolutionmedia.ai', 'jose.camacho@revolutionmedia.ai',
                    'luis.mariano@revolutionmedia.ai', 'nayeli.novoa@revolutionmedia.ai',
                    'eduardo.hernandez@revolutionmedia.ai', 'kenny.santaella@revolutionmedia.ai'
                ) THEN 'Slice'
                ELSE PlatformAccess
            END
            WHERE Email IS NOT NULL
            """, ct);

        // ============================================================================
        // 8. Constraints — only if data is clean
        // ============================================================================
        // Make RoleId NOT NULL if no nulls remain
        var roleIdNullsRaw = await ExecScalarAsync(connection,
            "SELECT COUNT(*) FROM alrrx_users WHERE RoleId IS NULL", ct);
        var roleIdNulls = roleIdNullsRaw is null or DBNull ? 0 : Convert.ToInt32(roleIdNullsRaw);
        if (roleIdNulls == 0)
        {
            await TryAlterAsync(connection,
                "ALTER TABLE alrrx_users MODIFY COLUMN RoleId INT NOT NULL", ct);
        }

        // Add FK constraints (idempotent — try/catch handles already-exists)
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD CONSTRAINT FK_alrrx_users_RoleId FOREIGN KEY (RoleId) REFERENCES alrrx_roles(Id)", ct);
        await TryAlterAsync(connection,
            "ALTER TABLE alrrx_users ADD CONSTRAINT FK_alrrx_users_ApprovedBy FOREIGN KEY (ApprovedBy) REFERENCES alrrx_users(Id)", ct);

        // ============================================================================
        // 9. Seed bootstrap users (4 hardcoded)
        // ============================================================================
        var admins = new (string Email, string Name, string Hash, string Role, string PlatformAccess)[]
        {
            ("kevin.escalante@revolutionmedia.ai", "Kevin Escalante", BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin",      "Both"),
            ("david@revolutionmedia.ai",          "David",           BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin",      "Both"),
            ("cuauhtemoc@revolutionmedia.ai",     "Cuauhtemoc",      BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin",      "Both"),
            ("j.lines@revolutionmedia.ai",         "Justin Lines",    BCrypt.Net.BCrypt.HashPassword("Admin123!"),  "Admin",      "Both"),
            ("jessica.duarte@revolutionmedia.ai", "Jessica Duarte",  BCrypt.Net.BCrypt.HashPassword("Super123!"),  "Supervisor", "Altrx"),
        };

        foreach (var (email, name, hash, role, platformAccess) in admins)
        {
            var existsRaw = await ExecScalarAsync(connection,
                "SELECT COUNT(*) FROM alrrx_users WHERE Email = @Email", ct,
                ("@Email", email));
            var exists = existsRaw is null or DBNull ? 0 : Convert.ToInt32(existsRaw);
            if (exists > 0) continue;

            var roleIdRaw = await ExecScalarAsync(connection,
                "SELECT Id FROM alrrx_roles WHERE Name = @Name", ct,
                ("@Name", role));
            if (roleIdRaw is null or DBNull) continue;

            var roleId = Convert.ToInt32(roleIdRaw);
            await ExecAsync(connection, """
                INSERT INTO alrrx_users
                    (Email, PasswordHash, FullName, Role, RoleId, Status, PlatformAccess, IsActive, ApprovedAt)
                VALUES
                    (@Email, @PasswordHash, @FullName, @Role, @RoleId, 'Active', @PlatformAccess, 1, @ApprovedAt)
                """, ct,
                ("@Email", email), ("@PasswordHash", hash), ("@FullName", name),
                ("@Role", role), ("@RoleId", roleId), ("@PlatformAccess", platformAccess),
                ("@ApprovedAt", DateTime.UtcNow));

            _logger.LogInformation("Seeded user: {Email} as {Role} ({PlatformAccess})", email, role, platformAccess);
        }
    }

    // ----------------------------------------------------------------------------
    // SQL helpers
    // ----------------------------------------------------------------------------
    private static async Task ExecAsync(MySqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> ExecScalarAsync(MySqlConnection conn, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync(ct);
    }

    private static async Task TryAlterAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            await using var cmd = new MySqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (MySqlException ex) when (ex.Number is 1060 or 1061 or 1050 or 1091 or 1062 or 1822)
        {
            // 1060: Duplicate column name
            // 1061: Duplicate key name (index already exists)
            // 1050: Table already exists
            // 1091: Cannot drop, doesn't exist
            // 1062: Duplicate entry (FK already exists)
            // 1822: Failed to add foreign key constraint (already exists)
            // Idempotent — already applied
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
        cmd.Parameters.AddWithValue("@PlatformAccess", user.PlatformAccess.ToString());
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
            INSERT INTO alrrx_users (Email, PasswordHash, FullName, RoleId, Role, Status, PlatformAccess, IsActive, CreatedBy)
            VALUES (@Email, @PasswordHash, @FullName, @RoleId, @Role, @Status, @PlatformAccess, @IsActive, @CreatedBy)
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
