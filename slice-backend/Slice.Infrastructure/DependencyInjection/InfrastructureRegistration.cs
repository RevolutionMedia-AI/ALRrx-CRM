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

/// <summary>
/// Registers all infrastructure services (auth, repositories, file processing, email).
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddSliceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Auth
        services.AddSingleton<IAuthService, JwtAuthService>();

        // User store — registered both as the concrete type (for seed service)
        // and as the interface (for controllers and middleware).
        services.AddSingleton<InMemoryUserRepository>();
        services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<InMemoryUserRepository>());

        // Repositories (in-memory; swap for DB implementations without changing callers)
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IReportRepository, InMemoryReportRepository>();

        // File processing
        services.AddScoped<IZipExtractionService, ZipExtractionService>();
        services.AddScoped<IExcelParserService, ExcelParserService>();
        services.AddScoped<IReportMergeService, ReportMergeService>();
        services.AddScoped<IFileProcessingOrchestrator, FileProcessingOrchestrator>();
        services.AddScoped<TemplateGeneratorService>();

        // User seed (runs on startup)
        services.AddHostedService<UserSeedService>();

        // Typed HttpClient for Resend email API
        services.AddHttpClient("Resend", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {configuration["Resend:ApiKey"]}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Named HttpClient for Google OAuth userinfo endpoint
        services.AddHttpClient("Google", client =>
        {
            client.BaseAddress = new Uri("https://www.googleapis.com/oauth2/v3/");
        });

        services.AddScoped<IEmailService, ResendEmailService>();

        return services;
    }
}
