using System.Globalization;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Middleware;

/// <summary>
/// LoggerMessage delegates for RateLimitMiddleware.
/// </summary>
internal static partial class RateLimitMiddlewareLogs
{
    [LoggerMessage(LogLevel.Debug, "Rate limiting request for key {Key}")]
    public static partial void RateLimitingRequest(ILogger logger, string key);

    [LoggerMessage(LogLevel.Warning, "Request rate limited for {Key}, returning 429")]
    public static partial void RequestRateLimited(ILogger logger, string key);
}

/// <summary>
/// Middleware that enforces rate limits on incoming requests.
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitConfig _config;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(
        RequestDelegate next,
        IOptions<RateLimitConfig> config,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _config = config.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IRateLimiter rateLimiter)
    {
        if (!_config.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        // Skip excluded paths
        if (ShouldExclude(path))
        {
            await _next(context);
            return;
        }

        // Determine rate limit key
        var key = GetRateLimitKey(context);

        RateLimitMiddlewareLogs.RateLimitingRequest(_logger, key);

        // Try to acquire permit
        var result = await rateLimiter.TryAcquireAsync(key);

        // Add rate limit headers
        if (_config.IncludeHeaders)
        {
            context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-RateLimit-Reset"] = result.ResetAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        if (!result.IsAllowed)
        {
            RateLimitMiddlewareLogs.RequestRateLimited(_logger, key);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)Math.Ceiling(result.RetryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

            await context.Response.WriteAsJsonAsync(new
            {
                error = "RATE_LIMIT_EXCEEDED",
                message = "Too many requests. Please try again later.",
                code = "RATE_LIMIT_EXCEEDED",
                retryAfter = (int)Math.Ceiling(result.RetryAfter.TotalSeconds)
            });

            return;
        }

        await _next(context);
    }

    private bool ShouldExclude(string path)
    {
        foreach (var excluded in _config.ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetRateLimitKey(HttpContext context)
    {
        // Priority: Project ID > API Key > IP Address

        // Try project ID from route
        if (context.Request.RouteValues.TryGetValue("projectId", out var projectIdValue))
        {
            var projectId = projectIdValue?.ToString();
            if (!string.IsNullOrEmpty(projectId))
            {
                return $"project:{projectId}";
            }
        }

        // Try project ID from header
        if (context.Request.Headers.TryGetValue("X-Project-Id", out var projectHeader))
        {
            var projectId = projectHeader.FirstOrDefault();
            if (!string.IsNullOrEmpty(projectId))
            {
                return $"project:{projectId}";
            }
        }

        // Fallback to IP address
        var ipAddress = GetClientIpAddress(context);
        return $"ip:{ipAddress}";
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded header first
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>
/// Extension methods for registering rate limit middleware.
/// </summary>
public static class RateLimitMiddlewareExtensions
{
    /// <summary>
    /// Adds the rate limit middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitMiddleware>();
    }
}
