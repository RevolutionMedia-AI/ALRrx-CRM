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

        var admins = new[]
        {
            ("kevin.escalante@revolutionmedia.ai", "Kevin Escalante", BCrypt.Net.BCrypt.HashPassword("Admin123!")),
            ("david@revolutionmedia.ai", "David", BCrypt.Net.BCrypt.HashPassword("Admin123!")),
            ("cuauhtemoc@revolutionmedia.ai", "Cuauhtemoc", BCrypt.Net.BCrypt.HashPassword("Admin123!")),
            ("jessica.duarte@revolutionmedia.ai", "Jessica Duarte", BCrypt.Net.BCrypt.HashPassword("Super123!")),
        };

        foreach (var (email, name, hash) in admins)
        {
            var role = email == "jessica.duarte@revolutionmedia.ai" ? "Supervisor" : "Admin";
            await using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM alrrx_users WHERE Email = @Email", connection);
            checkCmd.Parameters.AddWithValue("@Email", email);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync(ct)) > 0;
            if (exists) continue;

            await using var insertCmd = new MySqlCommand("""
                INSERT INTO alrrx_users (Email, PasswordHash, FullName, Role, IsActive)
                VALUES (@Email, @PasswordHash, @FullName, @Role, 1)
                """, connection);
            insertCmd.Parameters.AddWithValue("@Email", email);
            insertCmd.Parameters.AddWithValue("@PasswordHash", hash);
            insertCmd.Parameters.AddWithValue("@FullName", name);
            insertCmd.Parameters.AddWithValue("@Role", role);
            await insertCmd.ExecuteNonQueryAsync(ct);
            _logger.LogInformation("Seeded user: {Email} as {Role}", email, role);
        }
    }

    public async Task<AuthUser?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT * FROM alrrx_users WHERE Email = @Email", connection);
        cmd.Parameters.AddWithValue("@Email", email);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return MapUser(reader);
    }

    public async Task<AuthUser?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT * FROM alrrx_users WHERE Id = @Id", connection);
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        return MapUser(reader);
    }

    public async Task<List<AuthUser>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT * FROM alrrx_users ORDER BY CreatedAt DESC", connection);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var users = new List<AuthUser>();
        while (await reader.ReadAsync(ct))
            users.Add(MapUser(reader));

        return users;
    }

    public async Task CreateAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO alrrx_users (Email, PasswordHash, FullName, Role, IsActive, CreatedBy)
            VALUES (@Email, @PasswordHash, @FullName, @Role, @IsActive, @CreatedBy)
            """, connection);
        cmd.Parameters.AddWithValue("@Email", user.Email);
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@Role", user.Role.ToString());
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
        cmd.Parameters.AddWithValue("@CreatedBy", user.CreatedBy ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAsync(AuthUser user, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            UPDATE alrrx_users
            SET FullName = @FullName, PasswordHash = @PasswordHash, Role = @Role, IsActive = @IsActive
            WHERE Id = @Id
            """, connection);
        cmd.Parameters.AddWithValue("@FullName", user.FullName);
        cmd.Parameters.AddWithValue("@PasswordHash", user.PasswordHash);
        cmd.Parameters.AddWithValue("@Role", user.Role.ToString());
        cmd.Parameters.AddWithValue("@IsActive", user.IsActive);
        cmd.Parameters.AddWithValue("@Id", user.Id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static AuthUser MapUser(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt32("Id"),
        Email = reader.GetString("Email"),
        PasswordHash = reader.GetString("PasswordHash"),
        FullName = reader.GetString("FullName"),
        Role = Enum.Parse<UserRole>(reader.GetString("Role")),
        IsActive = reader.GetBoolean("IsActive"),
        CreatedBy = reader.IsDBNull(reader.GetOrdinal("CreatedBy")) ? null : reader.GetInt32("CreatedBy"),
        CreatedAt = reader.GetDateTime("CreatedAt")
    };
}
