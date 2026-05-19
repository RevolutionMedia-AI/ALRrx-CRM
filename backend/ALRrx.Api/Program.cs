using System.Text;
using ALRrx.Api.Hubs;
using ALRrx.Api.Middleware;
using ALRrx.Application.DependencyInjection;
using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using ALRrx.Infrastructure.Database;
using ALRrx.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

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

var connectionConfig = new ConnectionConfig
{
    Host = builder.Configuration["Ssh:Host"] ?? "localhost",
    Port = int.Parse(builder.Configuration["Ssh:Port"] ?? "22"),
    Username = builder.Configuration["Ssh:Username"] ?? "",
    Password = builder.Configuration["Ssh:Password"] ?? "",
    PrivateKeyPath = builder.Configuration["Ssh:PrivateKeyPath"] ?? "",
    PrivateKeyPassphrase = builder.Configuration["Ssh:PrivateKeyPassphrase"] ?? "",
    LocalPort = int.Parse(builder.Configuration["Ssh:LocalPort"] ?? "3307"),
    RemoteHost = builder.Configuration["Ssh:RemoteHost"] ?? "127.0.0.1",
    RemotePort = int.Parse(builder.Configuration["Ssh:RemotePort"] ?? "3306"),
    Database = builder.Configuration["Database:Name"] ?? "asterisk",
    DatabaseUser = builder.Configuration["Database:User"] ?? "",
    DatabasePassword = builder.Configuration["Database:Password"] ?? ""
};

builder.Services.AddInfrastructure(connectionConfig);
builder.Services.AddApplication();

builder.Services.AddSingleton<IAuthService, ALRrx.Infrastructure.Auth.AuthService>();
builder.Services.AddSingleton<MutationExecutor>(sp =>
{
    var dbConnection = sp.GetRequiredService<IDatabaseConnection>();
    var logger = sp.GetRequiredService<ILogger<MutationExecutor>>();
    var connection = (MySqlConnection)dbConnection.GetConnectionAsync().GetAwaiter().GetResult();
    return new MutationExecutor(connection, logger);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var dbConnection = sp.GetRequiredService<IDatabaseConnection>();
    var logger = sp.GetRequiredService<ILogger<UserRepository>>();
    var connection = (MySqlConnection)await dbConnection.GetConnectionAsync();
    var repo = new UserRepository(connection, logger);
    await repo.EnsureAdminSeededAsync();
}

app.UseCors();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");

app.Run();
