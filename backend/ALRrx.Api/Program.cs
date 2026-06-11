using System.Text;
using ALRrx.Api.Hubs;
using ALRrx.Api.Middleware;
using ALRrx.Application.DependencyInjection;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using ALRrx.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var formConnectionConfig = new FormConnectionConfig
{
    Host = cfg["FormDatabase:Host"] ?? "",
    Port = int.Parse(cfg["FormDatabase:Port"] ?? "3306"),
    Database = cfg["FormDatabase:Name"] ?? "",
    User = cfg["FormDatabase:User"] ?? "",
    Password = cfg["FormDatabase:Password"] ?? "",
};

builder.Services.AddInfrastructure(connectionConfig, formConnectionConfig);
builder.Services.AddApplication();

builder.Services.AddSingleton<IAuthService, ALRrx.Infrastructure.Auth.AuthService>();
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

using (var scope = app.Services.CreateScope())
{
    var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var vicidialRepo = scope.ServiceProvider.GetRequiredService<ALRrx.Application.Interfaces.IVicidialSalesRepository>();
    var seedLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await userRepo.EnsureAdminSeededAsync();
        seedLogger.LogInformation("Admin seed completed successfully.");
    }
    catch (Exception ex)
    {
        seedLogger.LogWarning(ex, "No se pudo crear/verificar tabla alrrx_users. " +
            "El usuario de BD no tiene permisos CREATE. La app continuará sin seed.");
    }
    try
    {
        await vicidialRepo.EnsureTableAsync();
        seedLogger.LogInformation("Vicidial form sales table ready.");
    }
    catch (Exception ex)
    {
        seedLogger.LogWarning(ex, "No se pudo crear/verificar tabla vicidial_form_sales. " +
            "El usuario de BD no tiene permisos CREATE. La app continuará sin esa tabla.");
    }
}

app.Run();
