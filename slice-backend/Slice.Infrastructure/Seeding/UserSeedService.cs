using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slice.Domain.Entities;
using Slice.Infrastructure.Repositories;

namespace Slice.Infrastructure.Seeding;

/// <summary>
/// Seeds the user whitelist from appsettings Slice:Users on startup.
/// Users provisioned here use Google OAuth; no password required.
/// </summary>
public sealed class UserSeedService : IHostedService
{
    private readonly InMemoryUserRepository _users;
    private readonly IConfiguration _config;
    private readonly ILogger<UserSeedService> _logger;

    public UserSeedService(InMemoryUserRepository users, IConfiguration config, ILogger<UserSeedService> logger)
    {
        _users = users;
        _config = config;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var entries = _config.GetSection("Slice:Users").Get<UserSeedEntry[]>() ?? [];

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Email)) continue;

            var user = new SliceUser
            {
                Email = entry.Email.ToLowerInvariant(),
                FullName = entry.FullName,
                Role = entry.Role,
                // No password — these users authenticate via Google OAuth only
                PasswordHash = string.Empty,
                IsActive = true,
            };

            if (_users.Add(user))
                _logger.LogInformation("Seeded user {Email} as {Role}", user.Email, user.Role);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed record UserSeedEntry(string Email, string FullName, string Role);
}
