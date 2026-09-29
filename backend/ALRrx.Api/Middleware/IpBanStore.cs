using System.Collections.Concurrent;

namespace ALRrx.Api.Middleware;

/// <summary>
/// Denylist for IPs that tripped the sale-intake rate limit. The built-in
/// fixed-window limiter forgets an IP the moment its window rolls, which a
/// caller can simply wait out, so the ban has to outlive the window.
///
/// Expiry is tracked with <see cref="Environment.TickCount64"/> rather than the
/// wall clock: an NTP step or a manual clock change must not unban an attacker
/// early or hold a legitimate IP for hours longer than intended.
/// </summary>
public sealed class IpBanStore : IDisposable
{
    private readonly ConcurrentDictionary<string, long> _bannedUntil = new();
    private readonly ILogger<IpBanStore> _logger;
    private readonly Timer _sweeper;

    public IpBanStore(ILogger<IpBanStore> logger)
    {
        _logger = logger;
        // Needed because the endpoint is unauthenticated: a caller rotating
        // source addresses could otherwise grow this dictionary without bound,
        // since an entry is otherwise only revisited when that IP calls again.
        _sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsBanned(string ip, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_bannedUntil.TryGetValue(ip, out var until)) return false;

        var remaining = TimeSpan.FromMilliseconds(until - Environment.TickCount64);
        if (remaining <= TimeSpan.Zero)
        {
            _bannedUntil.TryRemove(ip, out _);
            return false;
        }

        retryAfter = remaining;
        return true;
    }

    public void Ban(string ip, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(ip) || duration <= TimeSpan.Zero) return;
        var until = Environment.TickCount64 + (long)duration.TotalMilliseconds;

        // Keep the longest ban, so a repeat offender cannot shorten their own
        // by re-tripping the limiter with a shorter configured value.
        _bannedUntil.AddOrUpdate(ip, until, (_, current) => Math.Max(current, until));
        _logger.LogWarning("Banned IP {Ip} from sale intake for {Minutes} minutes", ip, duration.TotalMinutes);
    }

    public int Count => _bannedUntil.Count;

    private void Sweep()
    {
        var now = Environment.TickCount64;
        foreach (var (ip, until) in _bannedUntil)
        {
            if (until <= now) _bannedUntil.TryRemove(ip, out _);
        }
    }

    public void Dispose() => _sweeper.Dispose();
}
