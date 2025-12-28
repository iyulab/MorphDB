using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.RateLimiting;

/// <summary>
/// LoggerMessage delegates for MemoryRateLimiter.
/// </summary>
internal static partial class RateLimiterLogs
{
    [LoggerMessage(LogLevel.Debug, "Rate limit acquired for {Key}: {Remaining}/{Limit} remaining")]
    public static partial void RateLimitAcquired(ILogger logger, string key, int remaining, int limit);

    [LoggerMessage(LogLevel.Warning, "Rate limit exceeded for {Key}: retry after {RetryAfterSeconds}s")]
    public static partial void RateLimitExceeded(ILogger logger, string key, double retryAfterSeconds);

    [LoggerMessage(LogLevel.Debug, "Rate limit reset for {Key}")]
    public static partial void RateLimitReset(ILogger logger, string key);

    [LoggerMessage(LogLevel.Debug, "Cleaned up {Count} expired rate limit buckets")]
    public static partial void BucketsCleanedUp(ILogger logger, int count);
}

/// <summary>
/// In-memory rate limiter using token bucket algorithm.
/// </summary>
public sealed class MemoryRateLimiter : IRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly RateLimitConfig _config;
    private readonly ILogger<MemoryRateLimiter> _logger;
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    public MemoryRateLimiter(
        IOptions<RateLimitConfig> config,
        ILogger<MemoryRateLimiter> logger)
    {
        _config = config.Value;
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredBuckets, null, CleanupInterval, CleanupInterval);
    }

    /// <inheritdoc/>
    public ValueTask<RateLimitResult> TryAcquireAsync(
        string key,
        int permits = 1,
        CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            return ValueTask.FromResult(RateLimitResult.Allowed(
                _config.DefaultLimit,
                _config.DefaultLimit,
                DateTimeOffset.UtcNow.Add(_config.DefaultWindow)));
        }

        var bucket = _buckets.GetOrAdd(key, _ => CreateBucket(key));
        var result = bucket.TryConsume(permits);

        if (result.IsAllowed)
        {
            RateLimiterLogs.RateLimitAcquired(_logger, key, result.Remaining, result.Limit);
        }
        else
        {
            RateLimiterLogs.RateLimitExceeded(_logger, key, result.RetryAfter.TotalSeconds);
        }

        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public ValueTask<RateLimitStatus> GetStatusAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var bucket = _buckets.GetOrAdd(key, _ => CreateBucket(key));
        var status = bucket.GetStatus();

        return ValueTask.FromResult(new RateLimitStatus
        {
            Key = key,
            Available = status.available,
            Limit = status.limit,
            Window = _config.DefaultWindow,
            ResetAt = status.resetAt,
            RequestCount = status.limit - status.available
        });
    }

    /// <inheritdoc/>
    public ValueTask ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_buckets.TryRemove(key, out _))
        {
            RateLimiterLogs.RateLimitReset(_logger, key);
        }

        return ValueTask.CompletedTask;
    }

    private TokenBucket CreateBucket(string key)
    {
        var limit = _config.DefaultLimit;

        // Check for project-specific limit
        if (key.StartsWith("project:", StringComparison.OrdinalIgnoreCase))
        {
            var projectIdStr = key["project:".Length..];
            if (Guid.TryParse(projectIdStr.Split(':')[0], out var projectId))
            {
                if (_config.ProjectLimits.TryGetValue(projectId, out var projectLimit))
                {
                    limit = projectLimit;
                }
            }
        }

        return new TokenBucket(limit, _config.DefaultWindow);
    }

    private void CleanupExpiredBuckets(object? state)
    {
        var expiredKeys = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _buckets)
        {
            if (kvp.Value.IsExpired(now))
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _buckets.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            RateLimiterLogs.BucketsCleanedUp(_logger, expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    /// <summary>
    /// Token bucket implementation with sliding window.
    /// </summary>
    private sealed class TokenBucket
    {
        private readonly int _maxTokens;
        private readonly TimeSpan _window;
        private readonly object _lock = new();

        private int _tokens;
        private DateTimeOffset _lastRefill;
        private DateTimeOffset _lastAccess;

        public TokenBucket(int maxTokens, TimeSpan window)
        {
            _maxTokens = maxTokens;
            _window = window;
            _tokens = maxTokens;
            _lastRefill = DateTimeOffset.UtcNow;
            _lastAccess = DateTimeOffset.UtcNow;
        }

        public RateLimitResult TryConsume(int permits)
        {
            lock (_lock)
            {
                _lastAccess = DateTimeOffset.UtcNow;
                RefillTokens();

                var resetAt = _lastRefill.Add(_window);

                if (_tokens >= permits)
                {
                    _tokens -= permits;
                    return RateLimitResult.Allowed(_tokens, _maxTokens, resetAt);
                }

                var retryAfter = resetAt - DateTimeOffset.UtcNow;
                if (retryAfter < TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.FromSeconds(1);
                }

                return RateLimitResult.Denied(_maxTokens, retryAfter, resetAt);
            }
        }

        public (int available, int limit, DateTimeOffset resetAt) GetStatus()
        {
            lock (_lock)
            {
                RefillTokens();
                return (_tokens, _maxTokens, _lastRefill.Add(_window));
            }
        }

        public bool IsExpired(DateTimeOffset now)
        {
            lock (_lock)
            {
                // Consider expired if not accessed for 2x the window
                return now - _lastAccess > _window * 2;
            }
        }

        private void RefillTokens()
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _lastRefill;

            if (elapsed >= _window)
            {
                // Full refill
                _tokens = _maxTokens;
                _lastRefill = now;
            }
            else
            {
                // Partial refill based on time elapsed
                var refillAmount = (int)(elapsed.TotalMilliseconds / _window.TotalMilliseconds * _maxTokens);
                if (refillAmount > 0)
                {
                    _tokens = Math.Min(_maxTokens, _tokens + refillAmount);
                    _lastRefill = now;
                }
            }
        }
    }
}
