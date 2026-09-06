using System.Diagnostics;
using System.Text.Json;

namespace EchoLifestyle.Web.Middleware;

/// <summary>
/// Single place where unhandled exceptions are turned into a response.
///
/// Users and API callers get a correlation id and nothing else; the detail goes
/// to the log against the same id, so a support conversation can start with
/// "what does it say on the screen?" and end with the exact stack trace.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            _logger.LogError(
                ex,
                "Unhandled exception on {Method} {Path}. TraceId={TraceId}",
                context.Request.Method,
                context.Request.Path,
                traceId);

            if (context.Response.HasStarted)
            {
                // Too late to change the response; the log entry is what we have.
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            if (IsApiRequest(context))
            {
                await WriteJsonAsync(context, traceId, ex);
            }
            else
            {
                context.Response.Redirect($"/error/500?traceId={Uri.EscapeDataString(traceId)}");
            }
        }
    }

    private static bool IsApiRequest(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // jQuery AJAX and fetch calls asking for JSON get JSON back, not a redirect.
        if (string.Equals(
                context.Request.Headers.XRequestedWith,
                "XMLHttpRequest",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return context.Request.Headers.Accept.Any(
            value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
    }

    private async Task WriteJsonAsync(HttpContext context, string traceId, Exception ex)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            message = "Something went wrong while processing your request.",
            traceId,

            // Only in Development. Production responses never carry exception detail.
            detail = _environment.IsDevelopment() ? ex.ToString() : null,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
