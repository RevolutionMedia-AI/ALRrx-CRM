using System.Data.Common;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class MariaDbConnectionFactory : IDatabaseConnection
{
    private readonly ConnectionConfig _config;
    private readonly ISshTunnelService _tunnel;
    private readonly ILogger<MariaDbConnectionFactory> _logger;
    private MySqlConnection? _connection;

    public MariaDbConnectionFactory(
        ConnectionConfig config,
        ISshTunnelService tunnel,
        ILogger<MariaDbConnectionFactory> logger)
    {
        _config = config;
        _tunnel = tunnel;
        _logger = logger;
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        await _tunnel.ConnectAsync(ct);

        if (_connection is { State: System.Data.ConnectionState.Open })
            return _connection;

        var builder = new MySqlConnectionStringBuilder
        {
            Server = _config.DatabaseHost,
            Port = (uint)_config.DatabasePort,
            UserID = _config.DatabaseUser,
            Password = _config.DatabasePassword,
            Database = _config.Database,
            Pooling = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 10,
            ConnectionTimeout = 30
        };

        _connection = new MySqlConnection(builder.ConnectionString);
        await _connection.OpenAsync(ct);

        _logger.LogInformation("MariaDB connection established to database: {Database}", _config.Database);

        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
    }
}
