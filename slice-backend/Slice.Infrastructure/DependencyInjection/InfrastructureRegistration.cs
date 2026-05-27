using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slice.Application.Interfaces;
using Slice.Domain.Interfaces;
using Slice.Infrastructure.Auth;
using Slice.Infrastructure.Email;
using Slice.Infrastructure.Excel;
using Slice.Infrastructure.Processing;
using Slice.Infrastructure.Repositories;
using Slice.Infrastructure.Seeding;
using Slice.Infrastructure.Zip;

namespace Slice.Infrastructure.DependencyInjection;

public static class InfrastructureRegistration
{
    public static IServiceCollection AddSliceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Auth
        services.AddSingleton<IAuthService, JwtAuthService>();
        services.AddSingleton<InMemoryUserRepository>();

        // Repositories (in-memory; swap for DB implementations without changing callers)
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IReportRepository, InMemoryReportRepository>();

        // File processing
        services.AddScoped<IZipExtractionService, ZipExtractionService>();
        services.AddScoped<IExcelParserService, ExcelParserService>();
        services.AddScoped<IReportMergeService, ReportMergeService>();
        services.AddScoped<IFileProcessingOrchestrator, FileProcessingOrchestrator>();

        // User seed
        services.AddHostedService<UserSeedService>();

        // Email — typed HttpClient for Resend
        services.AddHttpClient("Resend", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["Resend:ApiKey"]}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddScoped<IEmailService, ResendEmailService>();

        return services;
    }
}
