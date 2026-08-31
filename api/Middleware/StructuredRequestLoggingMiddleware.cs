using System.Diagnostics;

namespace financesApi.middleware;

public sealed class StructuredRequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<StructuredRequestLoggingMiddleware> logger)
{
    private static readonly EventId RequestCompleted = new(1000, nameof(RequestCompleted));

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = context.TraceIdentifier,
        });

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var level = GetLevel(context.Request.Path, context.Response.StatusCode);
            logger.Log(level, RequestCompleted,
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {ElapsedMilliseconds} ms",
                context.Request.Method,
                context.Request.Path.Value ?? string.Empty,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static LogLevel GetLevel(PathString path, int statusCode)
    {
        var isProbe = path.StartsWithSegments("/status/live")
            || path.StartsWithSegments("/status/ready")
            || path.StartsWithSegments("/status/health");
        if (isProbe) return statusCode >= 500 ? LogLevel.Warning : LogLevel.Debug;
        if (statusCode >= 500) return LogLevel.Error;
        if (statusCode >= 400) return LogLevel.Warning;
        return LogLevel.Information;
    }
}
