using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class MutationExecutor
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<MutationExecutor> _logger;

    public MutationExecutor(IDatabaseConnection dbConnection, ILogger<MutationExecutor> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    private async Task<MySqlConnection> GetOpenConnectionAsync(CancellationToken ct)
    {
        return (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
    }

    public async Task<int> UpdateRowAsync(string table, string idColumn, object idValue, Dictionary<string, object?> updates, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        var setClauses = updates.Keys.Select(k => $"`{k}` = @{k}");
        var sql = $"UPDATE `{table}` SET {string.Join(", ", setClauses)} WHERE `{idColumn}` = @_id";

        await using var cmd = new MySqlCommand(sql, connection);
        foreach (var (key, value) in updates)
            cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@_id", idValue);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Updated {Rows} row(s) in {Table} where {Col}={Val}", rows, table, idColumn, idValue);
        return rows;
    }

    public async Task<int> DeleteRowAsync(string table, string idColumn, object idValue, CancellationToken ct = default)
    {
        await using var connection = await GetOpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            $"DELETE FROM `{table}` WHERE `{idColumn}` = @_id", connection);
        cmd.Parameters.AddWithValue("@_id", idValue);

        var rows = await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Deleted {Rows} row(s) from {Table} where {Col}={Val}", rows, table, idColumn, idValue);
        return rows;
    }
}
