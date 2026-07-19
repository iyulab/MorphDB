namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for rate limiting API requests.
/// Provides token bucket-based rate limiting with configurable limits.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire a permit for the specified key.
    /// </summary>
    /// <param name="key">The rate limit key (e.g., "project:{id}", "apikey:{id}").</param>
    /// <param name="permits">Number of permits to acquire. Default: 1.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating if the request is allowed.</returns>
    ValueTask<RateLimitResult> TryAcquireAsync(
        string key,
        int permits = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current rate limit status for a key.
    /// </summary>
    /// <param name="key">The rate limit key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current rate limit status.</returns>
    ValueTask<RateLimitStatus> GetStatusAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the rate limit for a key.
    /// </summary>
    /// <param name="key">The rate limit key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ResetAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a rate limit acquisition attempt.
/// </summary>
public readonly struct RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Remaining permits in the current window.
    /// </summary>
    public int Remaining { get; init; }

    /// <summary>
    /// Maximum permits per window.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Time until the rate limit resets.
    /// </summary>
    public TimeSpan RetryAfter { get; init; }

    /// <summary>
    /// Timestamp when the limit resets.
    /// </summary>
    public DateTimeOffset ResetAt { get; init; }

    /// <summary>
    /// Creates an allowed result.
    /// </summary>
    public static RateLimitResult Allowed(int remaining, int limit, DateTimeOffset resetAt) => new()
    {
        IsAllowed = true,
        Remaining = remaining,
        Limit = limit,
        RetryAfter = TimeSpan.Zero,
        ResetAt = resetAt
    };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static RateLimitResult Denied(int limit, TimeSpan retryAfter, DateTimeOffset resetAt) => new()
    {
        IsAllowed = false,
        Remaining = 0,
        Limit = limit,
        RetryAfter = retryAfter,
        ResetAt = resetAt
    };
}

/// <summary>
/// Current status of a rate limit.
/// </summary>
public sealed class RateLimitStatus
{
    /// <summary>
    /// The rate limit key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Current number of available permits.
    /// </summary>
    public int Available { get; init; }

    /// <summary>
    /// Maximum permits per window.
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Time window duration.
    /// </summary>
    public TimeSpan Window { get; init; }

    /// <summary>
    /// When the current window resets.
    /// </summary>
    public DateTimeOffset ResetAt { get; init; }

    /// <summary>
    /// Total requests in current window.
    /// </summary>
    public long RequestCount { get; init; }
}

/// <summary>
/// Configuration for rate limiting.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>
    /// Default requests per window. Default: 1000.
    /// </summary>
    public int DefaultLimit { get; set; } = 1000;

    /// <summary>
    /// Default time window. Default: 1 minute.
    /// </summary>
    public TimeSpan DefaultWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Per-project rate limit overrides.
    /// Key: project ID, Value: requests per window.
    /// </summary>
    public Dictionary<Guid, int> ProjectLimits { get; set; } = new();

    /// <summary>
    /// Per-endpoint rate limit multipliers.
    /// Key: endpoint pattern, Value: multiplier (e.g., 0.5 for half rate).
    /// </summary>
    public Dictionary<string, double> EndpointMultipliers { get; set; } = new();

    /// <summary>
    /// Whether to include rate limit headers in responses.
    /// </summary>
    public bool IncludeHeaders { get; set; } = true;

    /// <summary>
    /// Whether rate limiting is enabled. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Paths to exclude from rate limiting.
    /// </summary>
    public HashSet<string> ExcludedPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/live",
        "/health/ready",
        "/metrics"
    };
}
