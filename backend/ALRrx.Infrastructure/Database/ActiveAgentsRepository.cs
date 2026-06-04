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
            SELECT user, full_name
            FROM vicidial_users
            WHERE user_group = 'ALTRX'
              AND active = 'Y'
              AND full_name IS NOT NULL
              AND full_name <> ''
            ORDER BY full_name ASC
            """;

        await using var connection = (MySqlConnection)await _dbConnection.GetConnectionAsync(ct);
        await using var cmd = new MySqlCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var results = new List<ActiveAltrxAgentDto>();
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ActiveAltrxAgentDto
            {
                User = reader.GetString("user"),
                FullName = reader.GetString("full_name"),
            });
        }
        _logger.LogInformation("Loaded {Count} active ALTRX agents", results.Count);
        return results;
    }
}
