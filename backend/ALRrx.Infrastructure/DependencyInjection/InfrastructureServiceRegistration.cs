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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        ConnectionConfig connectionConfig,
        FormConnectionConfig formConnectionConfig)
    {
        services.AddSingleton(connectionConfig);
        services.AddSingleton(formConnectionConfig);

        services.AddSingleton<ISshTunnelService, SshTunnelService>();
        services.AddSingleton<IDatabaseConnection, MariaDbConnectionFactory>();
        services.AddSingleton<FormDbConnectionFactory>();
        services.AddSingleton<IQueryService, QueryExecutor>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<MutationExecutor>();
        services.AddSingleton<IDashboardPdfService, DashboardPdfService>();
        services.AddSingleton<IDashboardExcelService, DashboardExcelService>();
        services.AddSingleton<IPeriodComparisonExcelService, PeriodComparisonExcelService>();
        services.AddSingleton<ITwilioPdfService, TwilioPdfService>();
        services.AddSingleton<ITwilioExcelService, TwilioExcelService>();
        services.AddSingleton<IVicidialSalesRepository, VicidialSalesRepository>();
        services.AddSingleton<IActiveAgentsRepository, ActiveAgentsRepository>();
        services.AddSingleton<IVicidialLeadRepository, VicidialLeadRepository>();

        services.AddSingleton<IReportExportService, ExcelExportService>();
        services.AddSingleton<IReportExportService, CsvExportService>();
        services.AddSingleton<IReportExportService, PdfExportService>();

        return services;
    }
}
