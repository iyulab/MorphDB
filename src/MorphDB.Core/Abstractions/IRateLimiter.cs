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

/// <summary>
/// Service for tracking and managing API usage quotas.
/// </summary>
public interface IQuotaService
{
    /// <summary>
    /// Records usage for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="operation">The operation type.</param>
    /// <param name="units">Number of units consumed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordUsageAsync(
        Guid projectId,
        QuotaOperation operation,
        long units = 1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current usage for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="period">The period to query (current month if null).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current usage statistics.</returns>
    Task<QuotaUsage> GetUsageAsync(
        Guid projectId,
        DateTimeOffset? period = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a project has exceeded its quota.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="operation">The operation to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quota check result.</returns>
    Task<QuotaCheckResult> CheckQuotaAsync(
        Guid projectId,
        QuotaOperation operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quota limits for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quota limits.</returns>
    Task<QuotaLimits> GetLimitsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Types of quota operations to track.
/// </summary>
public enum QuotaOperation
{
    /// <summary>
    /// API request count.
    /// </summary>
    ApiRequest = 0,

    /// <summary>
    /// Data read operations.
    /// </summary>
    DataRead = 1,

    /// <summary>
    /// Data write operations.
    /// </summary>
    DataWrite = 2,

    /// <summary>
    /// Storage usage in bytes.
    /// </summary>
    Storage = 3,

    /// <summary>
    /// Bandwidth usage in bytes.
    /// </summary>
    Bandwidth = 4
}

/// <summary>
/// Current quota usage for a project.
/// </summary>
public sealed class QuotaUsage
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The billing period.
    /// </summary>
    public required DateTimeOffset Period { get; init; }

    /// <summary>
    /// API request count.
    /// </summary>
    public long ApiRequests { get; init; }

    /// <summary>
    /// Data read operations.
    /// </summary>
    public long DataReads { get; init; }

    /// <summary>
    /// Data write operations.
    /// </summary>
    public long DataWrites { get; init; }

    /// <summary>
    /// Storage usage in bytes.
    /// </summary>
    public long StorageBytes { get; init; }

    /// <summary>
    /// Bandwidth usage in bytes.
    /// </summary>
    public long BandwidthBytes { get; init; }

    /// <summary>
    /// When usage was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// Result of a quota check.
/// </summary>
public readonly struct QuotaCheckResult
{
    /// <summary>
    /// Whether the operation is allowed.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Current usage.
    /// </summary>
    public long CurrentUsage { get; init; }

    /// <summary>
    /// Maximum allowed.
    /// </summary>
    public long Limit { get; init; }

    /// <summary>
    /// Percentage of quota used.
    /// </summary>
    public double UsagePercentage => Limit > 0 ? (double)CurrentUsage / Limit * 100 : 0;

    /// <summary>
    /// When the quota resets.
    /// </summary>
    public DateTimeOffset ResetAt { get; init; }

    /// <summary>
    /// Creates an allowed result.
    /// </summary>
    public static QuotaCheckResult Allowed(long current, long limit, DateTimeOffset resetAt) => new()
    {
        IsAllowed = true,
        CurrentUsage = current,
        Limit = limit,
        ResetAt = resetAt
    };

    /// <summary>
    /// Creates a denied result.
    /// </summary>
    public static QuotaCheckResult Denied(long current, long limit, DateTimeOffset resetAt) => new()
    {
        IsAllowed = false,
        CurrentUsage = current,
        Limit = limit,
        ResetAt = resetAt
    };
}

/// <summary>
/// Quota limits for a project.
/// </summary>
public sealed class QuotaLimits
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Maximum API requests per month.
    /// </summary>
    public long MaxApiRequests { get; init; } = 100_000;

    /// <summary>
    /// Maximum data read operations per month.
    /// </summary>
    public long MaxDataReads { get; init; } = 1_000_000;

    /// <summary>
    /// Maximum data write operations per month.
    /// </summary>
    public long MaxDataWrites { get; init; } = 100_000;

    /// <summary>
    /// Maximum storage in bytes.
    /// </summary>
    public long MaxStorageBytes { get; init; } = 1_073_741_824; // 1 GB

    /// <summary>
    /// Maximum bandwidth per month in bytes.
    /// </summary>
    public long MaxBandwidthBytes { get; init; } = 10_737_418_240; // 10 GB

    /// <summary>
    /// Plan tier name.
    /// </summary>
    public string Tier { get; init; } = "free";
}
