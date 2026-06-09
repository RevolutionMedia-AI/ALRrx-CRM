using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using Slice.Application.DependencyInjection;
using Slice.Api.Middleware;
using Slice.Infrastructure.DependencyInjection;
using Slice.Infrastructure.Persistence;

// ─── EPPlus license ───────────────────────────────────────────────────────────
// Must be called once before any ExcelPackage is created.
ExcelPackage.License.SetNonCommercialPersonal("Slice");

// Log the build identifier once at startup so we can confirm which commit
// the deployed image is running (useful when the runtime log buffer is lossy).
var buildSha =
    Environment.GetEnvironmentVariable("GIT_SHA")
    ?? typeof(Program).Assembly.GetName().Version?.ToString()
    ?? "unknown";
Console.WriteLine($"[slice-api] build_sha={buildSha} startup={DateTime.UtcNow:O}");

var builder = WebApplication.CreateBuilder(args);

// ─── Controllers & Swagger ────────────────────────────────────────────────────
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Emit camelCase JSON so the React frontend can use idiomatic property names
        // without per-property [JsonPropertyName] attributes.
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy  = System.Text.Json.JsonNamingPolicy.CamelCase;
        // Serializar enums como strings en PascalCase (e.g. JobStatus.Completed → "Completed")
        // para que el frontend de React pueda matchearlos sin tener que mapear enteros.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Slice API", Version = "v1" });

    // Enables "Authorize" button in Swagger UI so testers can pass their JWT.
    c.AddSecurityDefinition("Bearer", new()
    {
        Name        = "Authorization",
        Type        = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = Microsoft.OpenApi.Models.ParameterLocation.Header,
    });
    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
});

// ─── Application & Infrastructure layers ─────────────────────────────────────
builder.Services.AddSliceApplication();
builder.Services.AddSliceInfrastructure(builder.Configuration);

// ─── JWT Authentication ───────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var key        = Encoding.UTF8.GetBytes(jwtSection["Key"]!);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(key),
            // ClockSkew = Zero: tokens expire exactly at ExpiresAt, no grace period.
            ClockSkew                = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

// ─── CORS ─────────────────────────────────────────────────────────────────────
// Open to any origin during development. Restrict to the frontend domain in production.
builder.Services.AddCors(o => o.AddPolicy("SlicePolicy", p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// ─── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ─── Middleware pipeline ──────────────────────────────────────────────────────
// ExceptionMiddleware must be first so it catches errors from all subsequent middleware.
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("SlicePolicy");
app.UseAuthentication();
app.UseAuthorization();

// SliceEmailGuardMiddleware runs after auth so the email claim is already populated.
app.UseMiddleware<SliceEmailGuardMiddleware>();

// Health check endpoint — useful for container liveness probes.
app.MapGet("/health", () => Results.Ok(new
{
    status   = "healthy",
    service  = "Slice API",
    buildSha = buildSha,
    startedAt = DateTime.UtcNow,
}));

// DEBUG: build-info endpoint — confirms which commit the deployed image is
// actually running. Hit `GET /api/slice/debug/build` (rewritten by nginx).
app.MapGet("/debug/build", () => Results.Ok(new
{
    buildSha  = buildSha,
    startedAt = DateTime.UtcNow,
    processId = Environment.ProcessId,
    machine   = Environment.MachineName,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
}));

// DEBUG: log every request to fileupload endpoints so we can confirm the
// deploy is exercising the new code path.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/fileupload"))
    {
        Console.WriteLine($"[slice-api] hit {ctx.Request.Method} {ctx.Request.Path} at {DateTime.UtcNow:O} buildSha={buildSha}");
    }
    await next();
});

app.MapControllers();

// ─── DB initialization ────────────────────────────────────────────────────────
// Apply pending EF Core migrations on startup so the .db schema is always
// up to date with the latest entity model. This is idempotent (no-op if
// there are no pending migrations) and runs after the host is built so
// scoped services like DbContext can be resolved.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SliceDbContext>();
    try
    {
        db.Database.Migrate();
        Console.WriteLine($"[slice-api] database migrated: {db.Database.GetConnectionString()}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[slice-api] FATAL: database migration failed: {ex.Message}");
        throw;
    }
}

// DEBUG: DB diagnostics endpoint — confirms the SQLite file path and the
// number of reports persisted. Useful to validate the persistent volume
// is mounted at the expected path.
app.MapGet("/debug/db", (SliceDbContext db) => Results.Ok(new
{
    connectionString = db.Database.GetConnectionString(),
    reportCount      = db.Reports.Count(),
    jobCount         = db.ProcessingJobs.Count(),
    dailyGlobalRows  = db.DailyGlobal.Count(),
    shopCallMetricsRows = db.ShopCallMetrics.Count(),
}));

app.Run();
