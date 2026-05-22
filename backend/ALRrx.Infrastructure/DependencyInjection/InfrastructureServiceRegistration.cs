using ALRrx.Application.Interfaces;
using ALRrx.Application.UseCases;
using ALRrx.Domain.Interfaces;
using ALRrx.Domain.ValueObjects;
using ALRrx.Infrastructure.Database;
using ALRrx.Infrastructure.Export;
using ALRrx.Infrastructure.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace ALRrx.Infrastructure.DependencyInjection;

public static class InfrastructureServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, ConnectionConfig connectionConfig)
    {
        services.AddSingleton(connectionConfig);

        services.AddSingleton<ISshTunnelService, SshTunnelService>();
        services.AddSingleton<IDatabaseConnection, MariaDbConnectionFactory>();
        services.AddSingleton<IQueryService, QueryExecutor>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<MutationExecutor>();
        services.AddSingleton<IDashboardPdfService, DashboardPdfService>();
        services.AddSingleton<IDashboardExcelService, DashboardExcelService>();

        services.AddSingleton<IReportExportService, ExcelExportService>();
        services.AddSingleton<IReportExportService, CsvExportService>();
        services.AddSingleton<IReportExportService, PdfExportService>();

        return services;
    }
}
