using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class VicidialUserLookup : IVicidialUserLookup
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<VicidialUserLookup> _logger;

    public VicidialUserLookup(IDatabaseConnection dbConnection, ILogger<VicidialUserLookup> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    public async Task<VicidialUserInfo?> GetActiveAltrxUserAsync(string user, CancellationToken ct = default)
    {
        const string sql = """
            SELECT user, full_name
            FROM vicidial_users
            WHERE user = @User
              AND user_group = 'ALTRX'
              AND active = 'Y'
            LIMIT 1
            """;

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.Add("@User", MySqlDbType.VarChar).Value = user;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new VicidialUserInfo(reader.GetString("user"), reader.GetString("full_name"));
    }
}
