using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Applies the same per-project rate limit HTTP requests already answer to <see cref="MorphHub"/>
/// method calls.
/// <para>
/// <see cref="RateLimitMiddleware"/> only sees the connection handshake — everything a client sends
/// after that travels over the open WebSocket and never re-enters the HTTP pipeline, so a connection
/// held open indefinitely could otherwise call <c>Subscribe</c>/<c>Unsubscribe</c> without limit.
/// Sharing the same <see cref="IRateLimiter"/> and key scheme (<c>project:{id}</c>) means a caller
/// cannot outrun its quota by switching from REST to the hub — both draw from the same bucket.
/// </para>
/// </summary>
internal sealed partial class HubRateLimitFilter : IHubFilter
{
    private readonly IRateLimiter _rateLimiter;
    private readonly RateLimitConfig _config;
    private readonly ILogger<HubRateLimitFilter> _logger;

    public HubRateLimitFilter(
        IRateLimiter rateLimiter,
        IOptions<RateLimitConfig> config,
        ILogger<HubRateLimitFilter> logger)
    {
        _rateLimiter = rateLimiter;
        _config = config.Value;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!_config.Enabled)
        {
            return await next(invocationContext);
        }

        var key = GetRateLimitKey(invocationContext.Context);
        var result = await _rateLimiter.TryAcquireAsync(key);

        if (!result.IsAllowed)
        {
            LogRateLimited(_logger, key, invocationContext.HubMethodName);
            throw new HubException(
                $"Rate limit exceeded. Retry after {result.RetryAfter.TotalSeconds:F0} second(s).");
        }

        return await next(invocationContext);
    }

    /// <summary>
    /// Every hub method already requires a project id at connect time
    /// (<see cref="MorphHub.GetProjectId"/> refuses the connection otherwise), so — unlike the HTTP
    /// middleware, which falls back to a caller IP for anonymous requests — a connection that
    /// reaches this filter always resolves to a project key.
    /// </summary>
    private static string GetRateLimitKey(HubCallerContext context)
    {
        var projectIdHeader = context.GetHttpContext()?.Request.Headers["X-Project-Id"].FirstOrDefault();
        return Guid.TryParse(projectIdHeader, out var projectId) && projectId != Guid.Empty
            ? $"project:{projectId}"
            : $"connection:{context.ConnectionId}";
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Hub method {HubMethodName} rate limited for {Key}")]
    private static partial void LogRateLimited(ILogger logger, string key, string hubMethodName);
}
