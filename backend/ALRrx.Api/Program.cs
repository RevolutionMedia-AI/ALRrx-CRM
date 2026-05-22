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
builder.WebHost.UseUrls($"http://+:{port}");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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

builder.Services.AddInfrastructure(connectionConfig);
builder.Services.AddApplication();

builder.Services.AddSingleton<IAuthService, ALRrx.Infrastructure.Auth.AuthService>();

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

app.Run();
