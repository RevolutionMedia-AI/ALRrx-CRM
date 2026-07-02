using System.Text;
using System.Threading.RateLimiting;
using ALRrx.Api.HostedServices;
using ALRrx.Api.Hubs;
using ALRrx.Api.Middleware;
using ALRrx.Application.DependencyInjection;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using ALRrx.Infrastructure.DependencyInjection;
using ALRrx.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
var aspnetUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (!string.IsNullOrEmpty(aspnetUrls))
{
    // Supervisord wins: ALRrx listens on :5000 inside the combined container.
    builder.WebHost.UseUrls(aspnetUrls);
}
else
{
    builder.WebHost.UseUrls($"http://+:{port}");
}

var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "*")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 1 && allowedOrigins[0] == "*")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var jwtKey = builder.Configuration["Jwt:Key"] ?? "super-secret-key-alrrx-2026-min-32-chars!";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ALRrx",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ALRrx",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IEmailService, ResendEmailService>();

var rlGlobal = builder.Configuration.GetValue<int>("RateLimiting:GlobalPerIpPerMinute", 600);
var rlAuth = builder.Configuration.GetValue<int>("RateLimiting:AuthPerIpPerMinute", 60);
var rlAuthCheck = builder.Configuration.GetValue<int>("RateLimiting:AuthCheckPerUserPerMinute", 120);
var rlVicidial = builder.Configuration.GetValue<int>("RateLimiting:VicidialPerIpPerMinute", 120);
var rlAdmin = builder.Configuration.GetValue<int>("RateLimiting:AdminPerUserPerMinute", 120);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
            ? ra.TotalSeconds.ToString("0") : "60";
        context.HttpContext.Response.Headers["Retry-After"] = retryAfter;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests — please slow down",
            retryAfterSeconds = int.Parse(retryAfter)
        }, ct);
    };

    // Global per-IP limit applied to every API request as a baseline.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rlGlobal,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    });

    // Per-IP: defends against brute force on /api/auth/login and burst attacks on /api/auth/google.
    // Per-device (X-Device-Fingerprint): secondary key so a single attacker with one device
    // can't exhaust the per-IP bucket and lock out other users behind the same NAT.
    options.AddPolicy("auth", ctx =>
    {
        var device = ctx.Request.Headers["X-Device-Fingerprint"].FirstOrDefault();
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        // The key is a composite so a single burst hits BOTH limits: the
        // device-scoped one (which the attacker can spoof trivially but
        // their real browser is bound to) and the IP-scoped one (which
        // protects against distributed brute force from many devices).
        var key = string.IsNullOrEmpty(device) ? $"ip:{ip}" : $"dev:{device}|ip:{ip}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rlAuth,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Per-user: applied to /api/auth/me (token validation). Each user has their
    // own bucket keyed on the JWT NameIdentifier claim, so 10 users behind
    // the same NAT/proxy don't share a single limit. Falls back to IP if
    // the token is missing (e.g. token refresh race).
    options.AddPolicy("authCheck", ctx =>
    {
        var userId = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = !string.IsNullOrEmpty(userId)
            ? $"u:{userId}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rlAuthCheck,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Per-IP: more permissive because Vicidial may legitimately send several in quick succession.
    options.AddPolicy("vicidial", ctx =>
    {
        var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rlVicidial,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });

    // Per-user: mitigates compromised-admin scenarios. Falls back to IP for unauthenticated requests.
    options.AddPolicy("admin", ctx =>
    {
        var userId = ctx.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var key = !string.IsNullOrEmpty(userId)
            ? $"u:{userId}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rlAdmin,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

var cfg = builder.Configuration;
var connectionConfig = new ConnectionConfig
{
    Host = cfg["Ssh:Host"] ?? "",
    Port = int.Parse(cfg["Ssh:Port"] ?? "0"),
    Username = cfg["Ssh:Username"] ?? "",
    Password = cfg["Ssh:Password"] ?? "",
    PrivateKeyPath = cfg["Ssh:PrivateKeyPath"] ?? "",
    PrivateKeyPassphrase = cfg["Ssh:PrivateKeyPassphrase"] ?? "",
    RemoteHost = cfg["Ssh:RemoteHost"] ?? "127.0.0.1",
    RemotePort = int.Parse(cfg["Ssh:RemotePort"] ?? "3306"),
    DatabaseHost = cfg["Database:Host"] ?? "127.0.0.1",
    DatabasePort = int.Parse(cfg["Database:Port"] ?? "3306"),
    Database = cfg["Database:Name"] ?? throw new InvalidOperationException("DB_NAME is required"),
    DatabaseUser = cfg["Database:User"] ?? throw new InvalidOperationException("DB_USER is required"),
    DatabasePassword = cfg["Database:Password"] ?? throw new InvalidOperationException("DB_PASSWORD is required"),
};

var crmConnectionConfig = new CrmConnectionConfig
{
    Host = cfg["CrmDatabase:Host"] ?? throw new InvalidOperationException("CrmDatabase:Host is required"),
    Port = int.Parse(cfg["CrmDatabase:Port"] ?? "3306"),
    Database = cfg["CrmDatabase:Name"] ?? throw new InvalidOperationException("CrmDatabase:Name is required"),
    User = cfg["CrmDatabase:User"] ?? throw new InvalidOperationException("CrmDatabase:User is required"),
    Password = cfg["CrmDatabase:Password"] ?? throw new InvalidOperationException("CrmDatabase:Password is required"),
};

builder.Services.AddInfrastructure(connectionConfig, crmConnectionConfig);
builder.Services.AddApplication();

builder.Services.AddSingleton<IAuthService, ALRrx.Infrastructure.Auth.AuthService>();
builder.Services.AddHostedService<DatabaseSeedHostedService>();
builder.Services.AddScoped<ALRrx.Application.Interfaces.ITwilioService, ALRrx.Infrastructure.Twilio.TwilioService>();

var app = builder.Build();

app.UseCors();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<UserStatusMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();
