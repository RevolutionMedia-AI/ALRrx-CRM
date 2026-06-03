using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class ActiveAgentsRepository : IActiveAgentsRepository
{
    private readonly IDatabaseConnection _dbConnection;
    private readonly ILogger<ActiveAgentsRepository> _logger;

    public ActiveAgentsRepository(IDatabaseConnection dbConnection, ILogger<ActiveAgentsRepository> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    public async Task<List<ActiveAltrxAgentDto>> GetActiveAltrxAgentsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                vla.user              AS User,
                vu.full_name          AS Name,
                vla.last_call_time    AS LastCallTime,
                vla.last_update_time  AS LastUpdateTime
            FROM vicidial_live_agents vla
            INNER JOIN vicidial_users vu ON vu.user = vla.user
            WHERE vu.user_group = 'ALTRX'
              AND vu.active    = 'Y'
            ORDER BY vu.full_name
            """;

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);

        var results = new List<ActiveAltrxAgentDto>();
        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(new ActiveAltrxAgentDto
                {
                    User = reader.GetString("User"),
                    Name = reader.GetString("Name"),
                    LastCallTime = reader.IsDBNull(reader.GetOrdinal("LastCallTime")) ? null : reader.GetDateTime("LastCallTime"),
                    LastUpdateTime = reader.IsDBNull(reader.GetOrdinal("LastUpdateTime")) ? null : reader.GetDateTime("LastUpdateTime"),
                });
            }
            return results;
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Failed to fetch active ALTRX agents. Code={Code}, Number={Number}", ex.ErrorCode, ex.Number);
            throw;
        }
    }
}
