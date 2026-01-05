namespace MorphDB.Service.Infrastructure;

/// <summary>
/// Middleware that tracks active requests for graceful shutdown.
/// </summary>
public sealed class RequestTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GracefulShutdownService _shutdownService;

    public RequestTrackingMiddleware(
        RequestDelegate next,
        GracefulShutdownService shutdownService)
    {
        _next = next;
        _shutdownService = shutdownService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip tracking for health checks to allow k8s probes during shutdown
        if (IsHealthCheck(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var requestId = context.TraceIdentifier;

        // Try to register the request
        if (!_shutdownService.TryRegisterRequest(requestId))
        {
            // Shutdown in progress and rejecting new requests
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.Append("Retry-After", "5");
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Service is shutting down",
                retryAfter = 5
            });
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            _shutdownService.CompleteRequest(requestId);
        }
    }

    private static bool IsHealthCheck(PathString path)
    {
        return path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/ready", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/live", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Extension methods for request tracking middleware.
/// </summary>
public static class RequestTrackingMiddlewareExtensions
{
    /// <summary>
    /// Adds request tracking middleware for graceful shutdown support.
    /// </summary>
    public static IApplicationBuilder UseRequestTracking(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RequestTrackingMiddleware>();
    }
}
