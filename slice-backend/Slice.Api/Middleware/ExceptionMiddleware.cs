using System.Text.Json;

namespace Slice.Api.Middleware;

/// <summary>
/// Global exception handler that converts unhandled exceptions to structured JSON error responses.
/// Maps <see cref="KeyNotFoundException"/> → 404, <see cref="InvalidOperationException"/> → 400,
/// <see cref="UnauthorizedAccessException"/> → 403, and all others → 500.
/// </summary>
public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteError(context, 404, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            await WriteError(context, 400, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteError(context, 403, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteError(context, 500, "An unexpected error occurred.");
        }
    }

    private static Task WriteError(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var body = JsonSerializer.Serialize(new { error = message });
        return ctx.Response.WriteAsync(body);
    }
}
