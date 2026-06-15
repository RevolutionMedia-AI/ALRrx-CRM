using ALRrx.Application.Interfaces;
using ALRrx.Domain.Entities;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly IDatabaseConnection _dbConnection;

    public AuditLogRepository(IDatabaseConnection dbConnection)
    {
        _dbConnection = dbConnection;
    }

    private async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        return (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
    }

    public async Task LogAsync(UserAuditLog entry, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO alrrx_user_audit_log
                (UserId, Action, PerformedBy, OldValue, NewValue, Reason, IpAddress)
            VALUES
                (@UserId, @Action, @PerformedBy, @OldValue, @NewValue, @Reason, @IpAddress)
            """, connection);
        cmd.Parameters.AddWithValue("@UserId", entry.UserId);
        cmd.Parameters.AddWithValue("@Action", entry.Action);
        cmd.Parameters.AddWithValue("@PerformedBy", (object?)entry.PerformedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OldValue", (object?)entry.OldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NewValue", (object?)entry.NewValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Reason", (object?)entry.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IpAddress", (object?)entry.IpAddress ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<UserAuditLog>> GetForUserAsync(int userId, int limit = 50, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT Id, UserId, Action, PerformedBy, OldValue, NewValue, Reason, IpAddress, CreatedAt
            FROM alrrx_user_audit_log
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC
            LIMIT @Limit
            """, connection);
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@Limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    public async Task<List<UserAuditLog>> GetRecentAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT Id, UserId, Action, PerformedBy, OldValue, NewValue, Reason, IpAddress, CreatedAt
            FROM alrrx_user_audit_log
            ORDER BY CreatedAt DESC
            LIMIT @Limit
            """, connection);
        cmd.Parameters.AddWithValue("@Limit", limit);
        return await ReadAllAsync(cmd, ct);
    }

    private static async Task<List<UserAuditLog>> ReadAllAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var list = new List<UserAuditLog>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new UserAuditLog
            {
                Id = reader.GetInt64("Id"),
                UserId = reader.GetInt32("UserId"),
                Action = reader.GetString("Action"),
                PerformedBy = reader.IsDBNull(reader.GetOrdinal("PerformedBy")) ? null : reader.GetInt32("PerformedBy"),
                OldValue = reader.IsDBNull(reader.GetOrdinal("OldValue")) ? null : reader.GetString("OldValue"),
                NewValue = reader.IsDBNull(reader.GetOrdinal("NewValue")) ? null : reader.GetString("NewValue"),
                Reason = reader.IsDBNull(reader.GetOrdinal("Reason")) ? null : reader.GetString("Reason"),
                IpAddress = reader.IsDBNull(reader.GetOrdinal("IpAddress")) ? null : reader.GetString("IpAddress"),
                CreatedAt = reader.GetDateTime("CreatedAt"),
            });
        }
        return list;
    }
}
