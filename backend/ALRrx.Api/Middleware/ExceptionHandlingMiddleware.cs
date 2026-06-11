using System.Net;
using System.Text.Json;

namespace ALRrx.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
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
            _logger.LogWarning(ex, "Resource not found");
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteErrorResponse(context, "Not Found", ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Bad request");
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorResponse(context, "Bad Request", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Invalid operation");
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await WriteErrorResponse(context, "Invalid Operation", ex.Message);
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteErrorResponse(context, "Internal Server Error", "An unexpected error occurred");
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, string title, string detail)
    {
        context.Response.ContentType = "application/json";
        // Return a flat string for `error` (not a nested object) so that
        // frontends that do `err.response.data.error` always get a string.
        var response = new { error = title, detail = detail };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
