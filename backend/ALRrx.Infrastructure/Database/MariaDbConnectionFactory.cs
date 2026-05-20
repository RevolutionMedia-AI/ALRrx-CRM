using System.Data.Common;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class MariaDbConnectionFactory : IDatabaseConnection
{
    private readonly ConnectionConfig _config;
    private readonly ISshTunnelService _tunnel;

    public MariaDbConnectionFactory(
        ConnectionConfig config,
        ISshTunnelService tunnel)
    {
        _config = config;
        _tunnel = tunnel;
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_config.Host))
            await _tunnel.ConnectAsync(ct);

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

        var connection = new MySqlConnection(builder.ConnectionString);
        var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        try
        {
            await connection.OpenAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Database connection timed out after 30s");
        }

        return connection;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
