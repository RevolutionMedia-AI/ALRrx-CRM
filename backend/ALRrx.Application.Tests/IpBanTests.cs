using System.Net;
using ALRrx.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ALRrx.Application.Tests;

// A ban that lifts too early is no protection; one that never lifts locks out
// real agents. These pin both edges, plus the scoping that keeps a 429 from
// another endpoint from banning anyone.
public class IpBanTests
{
    private static IpBanStore NewStore() => new(NullLogger<IpBanStore>.Instance);

    [Fact]
    public void Ban_blocks_that_ip()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromHours(1));

        Assert.True(store.IsBanned("10.0.0.1", out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
    }

    [Fact]
    public void Ban_is_per_ip()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromHours(1));

        Assert.False(store.IsBanned("10.0.0.2", out _));
    }

    [Fact]
    public void Ban_expires_and_frees_the_ip()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromMilliseconds(60));

        Assert.True(store.IsBanned("10.0.0.1", out _));
        Thread.Sleep(120);
        Assert.False(store.IsBanned("10.0.0.1", out _));
    }

    [Fact]
    public void Expired_ban_is_evicted_from_the_store()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromMilliseconds(30));
        Thread.Sleep(80);

        store.IsBanned("10.0.0.1", out _);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Re_ban_keeps_the_longest_window()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromHours(1));
        store.Ban("10.0.0.1", TimeSpan.FromMinutes(1));

        Assert.True(store.IsBanned("10.0.0.1", out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Middleware_turns_a_429_into_an_hour_long_ban()
    {
        using var store = NewStore();
        // Configured below the floor on purpose: the floor must win.
        var middleware = Build(store, minutes: 5, next: ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Task.CompletedTask;
        });

        var ctx = SaleRequest("10.0.0.1");
        await middleware.InvokeAsync(ctx);

        Assert.True(store.IsBanned("10.0.0.1", out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Banned_ip_gets_429_without_reaching_the_endpoint()
    {
        using var store = NewStore();
        store.Ban("10.0.0.1", TimeSpan.FromHours(1));

        var reached = false;
        var middleware = Build(store, minutes: 60, next: _ =>
        {
            reached = true;
            return Task.CompletedTask;
        });

        var ctx = SaleRequest("10.0.0.1");
        await middleware.InvokeAsync(ctx);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status429TooManyRequests, ctx.Response.StatusCode);
        Assert.True(int.TryParse(ctx.Response.Headers.RetryAfter, out var secs) && secs > 0);
    }

    [Fact]
    public async Task Allowed_ip_passes_through_untouched()
    {
        using var store = NewStore();
        var middleware = Build(store, minutes: 60, next: ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        var ctx = SaleRequest("10.0.0.1");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status200OK, ctx.Response.StatusCode);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task Non_sale_endpoint_is_never_banned()
    {
        using var store = NewStore();
        var middleware = Build(store, minutes: 60, next: ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return Task.CompletedTask;
        });

        // e.g. /api/auth/login tripping the "auth" limiter must not ban an IP
        // from the whole app.
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute("auth")),
            "login"));

        await middleware.InvokeAsync(ctx);

        Assert.Equal(0, store.Count);
    }

    private static IpBanMiddleware Build(IpBanStore store, int minutes, RequestDelegate next) =>
        new(
            next,
            store,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:VicidialSaleBanMinutes"] = minutes.ToString(),
                })
                .Build(),
            NullLogger<IpBanMiddleware>.Instance);

    private static DefaultHttpContext SaleRequest(string ip)
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(IpBanMiddleware.SalePolicy)),
            "sale"));
        return ctx;
    }
}
