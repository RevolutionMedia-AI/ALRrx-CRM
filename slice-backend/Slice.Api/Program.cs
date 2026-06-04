using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using Slice.Application.DependencyInjection;
using Slice.Api.Middleware;
using Slice.Infrastructure.DependencyInjection;

// ─── EPPlus license ───────────────────────────────────────────────────────────
// Must be called once before any ExcelPackage is created.
ExcelPackage.License.SetNonCommercialPersonal("Slice");

var builder = WebApplication.CreateBuilder(args);

// ─── Controllers & Swagger ────────────────────────────────────────────────────
builder.Services.AddControllers();
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
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Slice API" }));
app.MapControllers();

app.Run();
