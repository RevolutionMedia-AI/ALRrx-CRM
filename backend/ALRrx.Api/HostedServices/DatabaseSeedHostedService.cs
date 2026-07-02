using ALRrx.Application.Interfaces;

namespace ALRrx.Api.HostedServices;

public sealed class DatabaseSeedHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseSeedHostedService> _logger;

    public DatabaseSeedHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DatabaseSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database seed hosted service started (running in background after app startup).");

        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var vicidialRepo = scope.ServiceProvider.GetRequiredService<IVicidialSalesRepository>();

        try
        {
            await userRepo.EnsureAdminSeededAsync(stoppingToken);
            _logger.LogInformation("Admin seed completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo crear/verificar tabla alrrx_users. " +
                "El usuario de BD no tiene permisos CREATE. La app continuará sin seed.");
        }

        try
        {
            await vicidialRepo.EnsureTableAsync(stoppingToken);
            _logger.LogInformation("Vicidial form sales table ready.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo crear/verificar tabla vicidial_form_sales. " +
                "El usuario de BD no tiene permisos CREATE. La app continuará sin esa tabla.");
        }

        _logger.LogInformation("Database seed hosted service finished.");
    }
}
