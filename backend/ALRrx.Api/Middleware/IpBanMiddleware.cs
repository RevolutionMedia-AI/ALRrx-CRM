using Microsoft.AspNetCore.RateLimiting;

namespace ALRrx.Api.Middleware;

/// <summary>
/// Turns a 429 from the sale-intake rate limit into a multi-hour IP ban. Runs
/// immediately before <c>UseRateLimiter</c> so a banned caller is rejected
/// without spending quota, and so the limiter's own rejection is observable on
/// the way back out.
/// </summary>
public sealed class IpBanMiddleware
{
    /// <summary>Must match the policy name in Program.cs and on SubmitSale.</summary>
    public const string SalePolicy = "vicidial-sale";

    private const int MinBanMinutes = 60;

    private readonly RequestDelegate _next;
    private readonly IpBanStore _bans;
    private readonly ILogger<IpBanMiddleware> _logger;
    private readonly TimeSpan _banDuration;

    public IpBanMiddleware(
        RequestDelegate next,
        IpBanStore bans,
        IConfiguration config,
        ILogger<IpBanMiddleware> logger)
    {
        _next = next;
        _bans = bans;
        _logger = logger;
        // Floored at an hour: a ban that expires alongside the two-minute
        // window is not a ban, it is a suggestion.
        var minutes = config.GetValue("RateLimiting:VicidialSaleBanMinutes", MinBanMinutes);
        _banDuration = TimeSpan.FromMinutes(Math.Max(MinBanMinutes, minutes));
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Deliberately scoped to the sale endpoint. A 429 from any other policy
        // (login brute force, say) must not lock a real user out of the entire
        // app for an hour.
        var policy = ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        if (policy != SalePolicy)
        {
            await _next(ctx);
            return;
        }

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_bans.IsBanned(ip, out var retryAfter))
        {
            _logger.LogWarning("Rejected sale from banned IP {Ip}", ip);
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.Response.Headers.RetryAfter = seconds.ToString();
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "IP blocked for exceeding the sale request limit",
                retryAfterSeconds = seconds,
            });
            return;
        }

        await _next(ctx);

        // The limiter rejected this request, so start or extend the ban.
        if (ctx.Response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            _bans.Ban(ip, _banDuration);
        }
    }
}
