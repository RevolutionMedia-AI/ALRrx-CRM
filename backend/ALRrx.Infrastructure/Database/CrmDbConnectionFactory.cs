using System.Data.Common;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using MySqlConnector;

namespace ALRrx.Infrastructure.Database;

public sealed class CrmDbConnectionFactory : IDatabaseConnection
{
    private readonly CrmConnectionConfig _config;

    public CrmDbConnectionFactory(CrmConnectionConfig config)
    {
        _config = config;
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = _config.Host,
            Port = (uint)_config.Port,
            UserID = _config.User,
            Password = _config.Password,
            Database = _config.Database,
            Pooling = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 10,
            ConnectionTimeout = 30,
            SslMode = MySqlSslMode.Required,
            ConvertZeroDateTime = true,
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
            throw new TimeoutException("CRM database connection timed out after 30s");
        }

        return connection;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
