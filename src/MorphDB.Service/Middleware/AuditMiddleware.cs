using System.Diagnostics;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Middleware;

/// <summary>
/// Middleware that captures HTTP requests and responses for audit logging.
/// </summary>
public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;

    // Paths to exclude from audit logging
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/live",
        "/health/ready",
        "/metrics",
        "/swagger",
        "/favicon.ico"
    };

    public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditService auditService)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip excluded paths
        if (ShouldExclude(path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        Exception? exception = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            sw.Stop();

            // Try to get project ID from header or route
            var projectId = GetProjectId(context);

            if (projectId.HasValue)
            {
                var auditEvent = CreateAuditEvent(context, projectId.Value, sw.ElapsedMilliseconds, exception);
                await auditService.LogAsync(auditEvent);
            }
        }
    }

    private static bool ShouldExclude(string path)
    {
        foreach (var excluded in ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static Guid? GetProjectId(HttpContext context)
    {
        // Try route value first
        if (context.Request.RouteValues.TryGetValue("projectId", out var routeValue))
        {
            if (routeValue is Guid guid)
                return guid;
            if (routeValue is string str && Guid.TryParse(str, out guid))
                return guid;
        }

        // Try header
        if (context.Request.Headers.TryGetValue("X-Project-Id", out var headerValue))
        {
            if (Guid.TryParse(headerValue.FirstOrDefault(), out var guid))
                return guid;
        }

        return null;
    }

    private static AuditEvent CreateAuditEvent(
        HttpContext context,
        Guid projectId,
        long durationMs,
        Exception? exception)
    {
        var request = context.Request;
        var response = context.Response;
        var user = context.User;

        // Determine action from path and method
        var action = DetermineAction(request.Method, request.Path.Value ?? "");

        // Determine category
        var category = DetermineCategory(request.Path.Value ?? "");

        // Determine severity
        var severity = DetermineSeverity(response.StatusCode, exception);

        // Get actor info
        string? actorId = null;
        string? actorType = null;

        if (user.Identity?.IsAuthenticated == true)
        {
            actorId = user.FindFirst("sub")?.Value ?? user.FindFirst("user_id")?.Value;
            actorType = "user";
        }
        else if (context.Items.TryGetValue("ApiKeyId", out var apiKeyId))
        {
            actorId = apiKeyId?.ToString();
            actorType = "apikey";
        }

        // Extract resource info from path
        var (resourceType, resourceId) = ExtractResourceInfo(request.Path.Value ?? "");

        return new AuditEvent
        {
            ProjectId = projectId,
            Category = category,
            Action = action,
            Severity = severity,
            ActorId = actorId,
            ActorType = actorType,
            ResourceType = resourceType,
            ResourceId = resourceId,
            HttpMethod = request.Method,
            RequestPath = request.Path.Value,
            StatusCode = response.StatusCode,
            IpAddress = GetClientIpAddress(context),
            UserAgent = request.Headers.UserAgent.FirstOrDefault(),
            DurationMs = durationMs,
            ErrorMessage = exception?.Message
        };
    }

    private static string DetermineAction(string method, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length >= 2)
        {
            var resource = segments.Length >= 2 ? segments[^1] : segments[0];
            if (Guid.TryParse(resource, out _))
            {
                resource = segments.Length >= 2 ? segments[^2] : "resource";
            }

            return method.ToUpperInvariant() switch
            {
                "GET" => $"read_{resource}",
                "POST" => $"create_{resource}",
                "PUT" or "PATCH" => $"update_{resource}",
                "DELETE" => $"delete_{resource}",
                _ => $"{method.ToLowerInvariant()}_{resource}"
            };
        }

        return $"{method.ToLowerInvariant()}_request";
    }

    private static AuditCategory DetermineCategory(string path)
    {
        if (path.Contains("/auth", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/token", StringComparison.OrdinalIgnoreCase))
        {
            return AuditCategory.Auth;
        }

        if (path.Contains("/schema", StringComparison.OrdinalIgnoreCase))
        {
            return AuditCategory.Schema;
        }

        if (path.Contains("/data", StringComparison.OrdinalIgnoreCase))
        {
            return AuditCategory.Data;
        }

        if (path.Contains("/admin", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/projects", StringComparison.OrdinalIgnoreCase))
        {
            return AuditCategory.Admin;
        }

        if (path.Contains("/security", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/policies", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/keys", StringComparison.OrdinalIgnoreCase))
        {
            return AuditCategory.Security;
        }

        return AuditCategory.System;
    }

    private static AuditSeverity DetermineSeverity(int statusCode, Exception? exception)
    {
        if (exception is not null)
            return AuditSeverity.Error;

        return statusCode switch
        {
            >= 500 => AuditSeverity.Critical,
            >= 400 => AuditSeverity.Warning,
            _ => AuditSeverity.Info
        };
    }

    private static (string? resourceType, string? resourceId) ExtractResourceInfo(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (Guid.TryParse(segments[i], out _))
            {
                var resourceType = i > 0 ? segments[i - 1] : null;
                return (resourceType, segments[i]);
            }
        }

        return (null, null);
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded header first
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP if there are multiple
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// Extension methods for registering audit middleware.
/// </summary>
public static class AuditMiddlewareExtensions
{
    /// <summary>
    /// Adds the audit middleware to the pipeline.
    /// </summary>
    public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditMiddleware>();
    }
}
