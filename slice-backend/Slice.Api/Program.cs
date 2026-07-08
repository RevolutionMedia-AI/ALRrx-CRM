using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Slice.Application.DependencyInjection;
using Slice.Api.Auth;
using Slice.Api.Middleware;
using Slice.Infrastructure.Auth;
using Slice.Infrastructure.DependencyInjection;
using Slice.Infrastructure.Diagnostics;
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

// ─── Kestrel limits (defense in depth on body size and request lifetime) ────
// Without these, a single misbehaving client could pin a request thread for
// the full Kestrel keep-alive timeout (~2 minutes by default) and starve the
// thread pool — same root cause as the ALRrx login timeouts we saw on 0.1 vCPU.
builder.WebHost.ConfigureKestrel(k =>
{
    // Hard cap on the request body. The upload endpoints already set
    // [RequestSizeLimit] (600 MB for excel, 200 MB for zip) but we set a
    // global ceiling of 700 MB to allow a small margin and prevent OOM
    // attacks from non-upload paths.
    k.Limits.MaxRequestBodySize = 700L * 1024 * 1024;
    // Keep-alive timeout: a long keep-alive holds a connection slot
    // indefinitely. 30s is plenty for the React app's polling cadence.
    k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
    // Request header read timeout: ASP.NET defaults to 30s; if a client
    // opens a connection and never sends headers, we'd rather fail fast
    // and free the thread.
    k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});

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
// JwtKeyCache caches the SymmetricSecurityKey + TokenValidationParameters so
// JwtAuthService (signing) and JwtBearer (validation) don't re-encode the
// JWT key on every request. Built once at startup, then the singleton
// instance is reused for the lifetime of the process.
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey     = jwtSection["Key"]!;
var jwtIssuer  = jwtSection["Issuer"]!;
var jwtAudience = jwtSection["Audience"]!;

builder.Services.AddSingleton<SlicePerformanceMetrics>();
builder.Services.AddSingleton<JwtKeyCache>();
// Eagerly build + cache the validation parameters so the bearer middleware
// stores a stable, pre-built TokenValidationParameters reference.
var bootstrapMetrics = new SlicePerformanceMetrics();
var bootstrapKeyCache = new JwtKeyCache(bootstrapMetrics);
builder.Services.AddSingleton(_ => bootstrapKeyCache.GetValidationParameters(jwtKey, jwtIssuer, jwtAudience));

// Shared mutable allow list used by the email-guard middleware and the
// auth controller. Pre-populated from Slice:AllowedEmails/Domains config
// by the AuthController constructor.
builder.Services.AddSingleton<EmailAllowList>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = bootstrapKeyCache.GetValidationParameters(
            jwtKey, jwtIssuer, jwtAudience);
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

// Global request log so we can confirm export / template / debug requests
// actually reach the slice-api when nginx forwards them. Logs method, path,
// user-agent, status, elapsed ms.
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        await next();
        sw.Stop();
        var user = ctx.User?.Identity?.Name ?? "-";
        Console.WriteLine(
            $"[slice-api] {ctx.Request.Method} {ctx.Request.Path} -> {ctx.Response.StatusCode} ({sw.ElapsedMilliseconds}ms) user={user} ua={ctx.Request.Headers.UserAgent}");
    }
    catch (Exception ex)
    {
        sw.Stop();
        Console.WriteLine(
            $"[slice-api] {ctx.Request.Method} {ctx.Request.Path} -> EX ({sw.ElapsedMilliseconds}ms) {ex.GetType().Name}: {ex.Message}");
        throw;
    }
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

// Apply the runtime SQLite PRAGMAs (WAL, cache, mmap) AFTER migrations have
// run so the connection is fully open. These are safe to re-apply on every
// boot — SQLite PRAGMAs are idempotent.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SliceDbContext>();
    try
    {
        var stats = await db.ApplyPerformancePragmasAsync();
        Console.WriteLine(
            $"[slice-api] SQLite pragmas applied: journal={stats.JournalMode} sync={stats.Synchronous} " +
            $"cache={stats.CacheSize} mmap={stats.MmapSize} pages={stats.PageCount} size={stats.DbSizeBytes / 1024}KB");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[slice-api] WARN: failed to apply SQLite PRAGMAs: {ex.Message}");
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

// DEBUG: Performance dashboard. Returns PRAGMA values, query counts, slow
// query log, JWT cache hits/misses, and the Kestrel + thread pool settings.
// This is the endpoint to hit after the optimization pass to confirm the
// production instance is running with the tuned settings.
app.MapGet("/debug/perf", async (
    SliceDbContext db,
    SlicePerformanceMetrics metrics,
    JwtKeyCache keyCache) =>
{
    var pragma = await db.ApplyPerformancePragmasAsync();
    var snap   = metrics.Snapshot();
    var qs     = new
    {
        efQueries       = snap.EfQueryCount,
        efAvgMs         = snap.EfQueryCount == 0 ? 0 : (double)snap.EfQueryTotalTicks / snap.EfQueryCount / Stopwatch.Frequency * 1000,
        efMaxMs         = snap.EfQueryMaxTicks / (double)Stopwatch.Frequency * 1000,
        efTotalMs       = snap.EfQueryTotalTicks / (double)Stopwatch.Frequency * 1000,
    };
    var js = new
    {
        signs     = snap.JwtSignCount,
        avgMs     = snap.JwtSignCount == 0 ? 0 : (double)snap.JwtSignTotalTicks / snap.JwtSignCount / Stopwatch.Frequency * 1000,
        totalMs   = snap.JwtSignTotalTicks / (double)Stopwatch.Frequency * 1000,
        cacheHits = snap.JwtCacheHits,
        misses    = snap.JwtCacheMisses,
    };
    var tp = new
    {
        workerThreads    = ThreadPool.ThreadCount,
        completionPorts  = ThreadPool.PendingWorkItemCount,
    };
    var slow = snap.SlowQueries
        .OrderByDescending(q => q.ticks)
        .Select(q => new { ms = q.ticks / (double)Stopwatch.Frequency * 1000, tag = q.tag, at = q.at })
        .ToArray();
    return Results.Ok(new
    {
        buildSha     = buildSha,
        startedAt    = app.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted,
        uptimeSec    = (DateTime.UtcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds,
        sqlite       = new
        {
            journalMode = pragma.JournalMode,
            synchronous = pragma.Synchronous,
            cacheSize   = pragma.CacheSize,
            mmapSize    = pragma.MmapSize,
            tempStore   = pragma.TempStore,
            pageCount   = pragma.PageCount,
            pageSize    = pragma.PageSize,
            dbSizeMb    = pragma.DbSizeBytes / 1024d / 1024d,
        },
        efQueries    = qs,
        jwt          = js,
        jwtBuilds    = keyCache.BuildCount,
        threadPool   = tp,
        slowQueries  = slow,
    });
});

app.Run();
