using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using ALRrx.Infrastructure.Database;
using ALRrx.Infrastructure.Export;
using ALRrx.Infrastructure.Ssh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System.Data.Common;

namespace ALRrx.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, ConnectionConfig connectionConfig)
    {
        services.AddSingleton(connectionConfig);

        services.AddSingleton<ISshTunnelService, SshTunnelService>();
        services.AddSingleton<IDatabaseConnection, MariaDbConnectionFactory>();

        services.AddSingleton<Domain.Interfaces.IQueryService>(sp =>
        {
            var dbConnection = sp.GetRequiredService<IDatabaseConnection>();
            var logger = sp.GetRequiredService<ILogger<QueryExecutor>>();
            try
            {
                var connection = (MySqlConnection)dbConnection.GetConnectionAsync().GetAwaiter().GetResult();
                return new QueryExecutor(connection, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "QueryExecutor deferred — no database connection available yet");
                throw;
            }
        });

        services.AddSingleton<IUserRepository>(sp =>
        {
            var dbConnection = sp.GetRequiredService<IDatabaseConnection>();
            var logger = sp.GetRequiredService<ILogger<UserRepository>>();
            try
            {
                var connection = (MySqlConnection)dbConnection.GetConnectionAsync().GetAwaiter().GetResult();
                return new UserRepository(connection, logger);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UserRepository deferred — no database connection available yet");
                throw;
            }
        });

        services.AddSingleton<IReportExportService, ExcelExportService>();
        services.AddSingleton<IReportExportService, CsvExportService>();
        services.AddSingleton<IReportExportService, PdfExportService>();

        return services;
    }
}
