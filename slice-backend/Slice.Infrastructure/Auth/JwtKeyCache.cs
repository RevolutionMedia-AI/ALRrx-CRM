using System.Diagnostics;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Slice.Infrastructure.Diagnostics;

namespace Slice.Infrastructure.Auth;

/// <summary>
/// Lazy, process-wide cache for the <see cref="SymmetricSecurityKey"/> used to
/// sign and validate JWTs. The key is derived from the configured
/// <c>Jwt:Key</c> bytes — caching it avoids re-encoding the same UTF-8 bytes
/// on every request (the previous implementation allocated a new
/// <c>SymmetricSecurityKey</c> per login, which is wasteful).
/// Also caches the <see cref="TokenValidationParameters"/> so the JwtBearer
/// middleware doesn't re-build it on every request.
/// </summary>
public sealed class JwtKeyCache
{
    private readonly object _lock = new();
    private readonly SlicePerformanceMetrics _metrics;

    private string? _configuredKey;
    private string? _configuredIssuer;
    private string? _configuredAudience;
    private SymmetricSecurityKey? _signingKey;
    private TokenValidationParameters? _validationParameters;
    private int _builds; // increments on every key reconfiguration

    public JwtKeyCache(SlicePerformanceMetrics metrics) => _metrics = metrics;

    /// <summary>Gets the signing key, building it on first use or whenever the configured key changes.</summary>
    public SymmetricSecurityKey GetSigningKey(string configuredKey)
    {
        EnsureInitialized(configuredKey);
        return _signingKey!;
    }

    /// <summary>
    /// Gets the full <see cref="TokenValidationParameters"/> object, including
    /// issuer / audience / clock-skew settings. Cached for the lifetime of
    /// the process so we don't re-parse the config on every request.
    /// </summary>
    public TokenValidationParameters GetValidationParameters(
        string configuredKey,
        string issuer,
        string audience)
    {
        EnsureInitialized(configuredKey);
        if (_validationParameters is not null
            && _configuredIssuer == issuer
            && _configuredAudience == audience)
        {
            return _validationParameters;
        }
        lock (_lock)
        {
            if (_validationParameters is null
                || _configuredIssuer != issuer
                || _configuredAudience != audience)
            {
                _validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = issuer,
                    ValidAudience            = audience,
                    IssuerSigningKey         = _signingKey,
                    // Zero clock-skew: tokens expire exactly at ExpiresAt.
                    ClockSkew                = TimeSpan.Zero,
                };
                _configuredIssuer   = issuer;
                _configuredAudience = audience;
            }
            return _validationParameters;
        }
    }

    /// <summary>Number of times the signing key has been built (1 in normal operation, more if config changes at runtime).</summary>
    public int BuildCount => _builds;

    private void EnsureInitialized(string configuredKey)
    {
        if (_configuredKey == configuredKey && _signingKey is not null) return;
        lock (_lock)
        {
            if (_configuredKey == configuredKey && _signingKey is not null) return;
            var sw = Stopwatch.StartNew();
            _configuredKey = configuredKey;
            _signingKey    = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredKey));
            _builds++;
            sw.Stop();
            // First access is a "miss" (we had to build), subsequent are "hits".
            _metrics.RecordJwtSign(sw.ElapsedTicks, cacheHit: false);
        }
    }
}
