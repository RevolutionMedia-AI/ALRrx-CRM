using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Slice.Domain.Entities;
using Slice.Infrastructure.Repositories;

namespace Slice.Infrastructure.Seeding;

/// <summary>
/// Hosted service that populates the in-memory user store on startup
/// from the <c>Slice:Users</c> configuration section in <c>appsettings.json</c>.
/// Seeded users authenticate exclusively via Google OAuth (no password hash is stored).
/// Runs once at startup and does nothing on stop.
/// </summary>
public sealed class UserSeedService : IHostedService
{
    private readonly InMemoryUserRepository _users;
    private readonly IConfiguration         _config;
    private readonly ILogger<UserSeedService> _logger;

    public UserSeedService(
        InMemoryUserRepository users,
        IConfiguration config,
        ILogger<UserSeedService> logger)
    {
        _users  = users;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Reads all entries from <c>Slice:Users[]</c> and inserts them into the user store.
    /// Entries with a blank email are silently skipped. Duplicate emails are ignored
    /// (the store's <see cref="InMemoryUserRepository.Add"/> returns false on collision).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var entries = _config.GetSection("Slice:Users").Get<UserSeedEntry[]>() ?? [];

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Email)) continue;

            var user = new SliceUser
            {
                Email        = entry.Email.ToLowerInvariant(),
                FullName     = entry.FullName,
                Role         = entry.Role,
                PasswordHash = string.Empty, // Google OAuth only — no password required
                IsActive     = true,
            };

            if (_users.Add(user))
                _logger.LogInformation("Seeded user {Email} as {Role}", user.Email, user.Role);
        }

        return Task.CompletedTask;
    }

    /// <summary>No cleanup required — in-memory store needs no teardown.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Maps a single entry from the <c>Slice:Users</c> configuration array.</summary>
    private sealed record UserSeedEntry(string Email, string FullName, string Role);
}
