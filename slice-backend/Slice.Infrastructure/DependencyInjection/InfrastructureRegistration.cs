using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slice.Application.Interfaces;
using Slice.Domain.Interfaces;
using Slice.Infrastructure.Auth;
using Slice.Infrastructure.Diagnostics;
using Slice.Infrastructure.Email;
using Slice.Infrastructure.Excel;
using Slice.Infrastructure.Persistence;
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
        services.AddSingleton<JwtKeyCache>();
        services.AddSingleton<EfQueryMetricsInterceptor>();
        services.AddSingleton<IAuthService, JwtAuthService>();

        // User store — registered both as the concrete type (for seed service)
        // and as the interface (for controllers and middleware).
        services.AddSingleton<InMemoryUserRepository>();
        services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<InMemoryUserRepository>());

        // EF Core + SQLite for persistent report storage. The default connection
        // string points at /data/slice.db, which the Dockerfile/Docker Compose
        // maps to a persistent volume. Override in appsettings for tests.
        var connectionString = configuration["Slice:Database:ConnectionString"]
            ?? "Data Source=/data/slice.db";
        services.AddDbContext<SliceDbContext>((sp, opts) =>
        {
            opts.UseSqlite(connectionString, sqlite =>
            {
                // Bump the EF command timeout above the SQLite default of 30s.
                // Some periodic summary queries (with 4 child collections) can
                // exceed 30s on cold cache; we still want a hard ceiling to
                // fail fast rather than hang the request thread.
                sqlite.CommandTimeout(60);
            });
            // EF query interceptor that times every command and pushes the
            // measurement into SlicePerformanceMetrics for the /debug/perf
            // endpoint. Resolved from DI so the interceptor shares the same
            // singleton counter store as the rest of the app.
            opts.AddInterceptors(sp.GetRequiredService<EfQueryMetricsInterceptor>());
            // Enable sensitive-data logging only in dev — we don't want JWT
            // claims or report bodies in production logs.
#if DEBUG
            opts.EnableSensitiveDataLogging();
#endif
            opts.EnableDetailedErrors();
        });
        services.AddScoped<IReportRepository, EfReportRepository>();
        // Job repository still uses in-memory: jobs are short-lived and we don't
        // need historical queries on them.
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();

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
