using System.Collections.Concurrent;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.RateLimiting;

/// <summary>
/// LoggerMessage delegates for MemoryQuotaService.
/// </summary>
internal static partial class QuotaServiceLogs
{
    [LoggerMessage(LogLevel.Debug, "Recording usage for project {ProjectId}: {Operation} +{Units}")]
    public static partial void RecordingUsage(ILogger logger, Guid projectId, QuotaOperation operation, long units);

    [LoggerMessage(LogLevel.Warning, "Quota exceeded for project {ProjectId}: {Operation} ({Current}/{Limit})")]
    public static partial void QuotaExceeded(ILogger logger, Guid projectId, QuotaOperation operation, long current, long limit);

    [LoggerMessage(LogLevel.Debug, "Cleaned up {Count} expired quota entries")]
    public static partial void QuotaEntriesCleanedUp(ILogger logger, int count);
}

/// <summary>
/// In-memory quota tracking service.
/// Tracks API usage per project with monthly reset.
/// </summary>
public sealed class MemoryQuotaService : IQuotaService, IDisposable
{
    private readonly ConcurrentDictionary<string, QuotaEntry> _quotas = new();
    private readonly ConcurrentDictionary<Guid, QuotaLimits> _limits = new();
    private readonly ILogger<MemoryQuotaService> _logger;
    private readonly Timer _cleanupTimer;

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    public MemoryQuotaService(ILogger<MemoryQuotaService> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, CleanupInterval, CleanupInterval);
    }

    /// <inheritdoc/>
    public Task RecordUsageAsync(
        Guid projectId,
        QuotaOperation operation,
        long units = 1,
        CancellationToken cancellationToken = default)
    {
        QuotaServiceLogs.RecordingUsage(_logger, projectId, operation, units);

        var key = GetKey(projectId, GetCurrentPeriod());
        var entry = _quotas.GetOrAdd(key, _ => new QuotaEntry(projectId, GetCurrentPeriod()));

        entry.AddUsage(operation, units);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<QuotaUsage> GetUsageAsync(
        Guid projectId,
        DateTimeOffset? period = null,
        CancellationToken cancellationToken = default)
    {
        var targetPeriod = period ?? GetCurrentPeriod();
        var key = GetKey(projectId, targetPeriod);

        if (_quotas.TryGetValue(key, out var entry))
        {
            return Task.FromResult(entry.ToUsage());
        }

        return Task.FromResult(new QuotaUsage
        {
            ProjectId = projectId,
            Period = targetPeriod,
            ApiRequests = 0,
            DataReads = 0,
            DataWrites = 0,
            StorageBytes = 0,
            BandwidthBytes = 0,
            LastUpdated = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc/>
    public Task<QuotaCheckResult> CheckQuotaAsync(
        Guid projectId,
        QuotaOperation operation,
        CancellationToken cancellationToken = default)
    {
        var limits = GetLimitsSync(projectId);
        var period = GetCurrentPeriod();
        var key = GetKey(projectId, period);

        long current = 0;
        if (_quotas.TryGetValue(key, out var entry))
        {
            current = entry.GetUsage(operation);
        }

        var limit = GetLimit(limits, operation);
        var resetAt = GetNextPeriod();

        if (current >= limit)
        {
            QuotaServiceLogs.QuotaExceeded(_logger, projectId, operation, current, limit);
            return Task.FromResult(QuotaCheckResult.Denied(current, limit, resetAt));
        }

        return Task.FromResult(QuotaCheckResult.Allowed(current, limit, resetAt));
    }

    /// <inheritdoc/>
    public Task<QuotaLimits> GetLimitsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetLimitsSync(projectId));
    }

    /// <summary>
    /// Sets quota limits for a project.
    /// </summary>
    public void SetLimits(Guid projectId, QuotaLimits limits)
    {
        _limits[projectId] = limits;
    }

    private QuotaLimits GetLimitsSync(Guid projectId)
    {
        if (_limits.TryGetValue(projectId, out var limits))
        {
            return limits;
        }

        // Return default limits
        return new QuotaLimits { ProjectId = projectId };
    }

    private static long GetLimit(QuotaLimits limits, QuotaOperation operation)
    {
        return operation switch
        {
            QuotaOperation.ApiRequest => limits.MaxApiRequests,
            QuotaOperation.DataRead => limits.MaxDataReads,
            QuotaOperation.DataWrite => limits.MaxDataWrites,
            QuotaOperation.Storage => limits.MaxStorageBytes,
            QuotaOperation.Bandwidth => limits.MaxBandwidthBytes,
            _ => long.MaxValue
        };
    }

    private static string GetKey(Guid projectId, DateTimeOffset period)
    {
        return $"{projectId}:{period:yyyy-MM}";
    }

    private static DateTimeOffset GetCurrentPeriod()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset GetNextPeriod()
    {
        var current = GetCurrentPeriod();
        return current.AddMonths(1);
    }

    private void CleanupExpiredEntries(object? state)
    {
        var expiredKeys = new List<string>();
        var cutoff = GetCurrentPeriod().AddMonths(-2); // Keep 2 months of history

        foreach (var kvp in _quotas)
        {
            if (kvp.Value.Period < cutoff)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _quotas.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            QuotaServiceLogs.QuotaEntriesCleanedUp(_logger, expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    /// <summary>
    /// Internal quota entry for tracking usage.
    /// </summary>
    private sealed class QuotaEntry
    {
        private readonly object _lock = new();
        private long _apiRequests;
        private long _dataReads;
        private long _dataWrites;
        private long _storageBytes;
        private long _bandwidthBytes;
        private DateTimeOffset _lastUpdated;

        public Guid ProjectId { get; }
        public DateTimeOffset Period { get; }

        public QuotaEntry(Guid projectId, DateTimeOffset period)
        {
            ProjectId = projectId;
            Period = period;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void AddUsage(QuotaOperation operation, long units)
        {
            lock (_lock)
            {
                switch (operation)
                {
                    case QuotaOperation.ApiRequest:
                        Interlocked.Add(ref _apiRequests, units);
                        break;
                    case QuotaOperation.DataRead:
                        Interlocked.Add(ref _dataReads, units);
                        break;
                    case QuotaOperation.DataWrite:
                        Interlocked.Add(ref _dataWrites, units);
                        break;
                    case QuotaOperation.Storage:
                        Interlocked.Add(ref _storageBytes, units);
                        break;
                    case QuotaOperation.Bandwidth:
                        Interlocked.Add(ref _bandwidthBytes, units);
                        break;
                }
                _lastUpdated = DateTimeOffset.UtcNow;
            }
        }

        public long GetUsage(QuotaOperation operation)
        {
            return operation switch
            {
                QuotaOperation.ApiRequest => Interlocked.Read(ref _apiRequests),
                QuotaOperation.DataRead => Interlocked.Read(ref _dataReads),
                QuotaOperation.DataWrite => Interlocked.Read(ref _dataWrites),
                QuotaOperation.Storage => Interlocked.Read(ref _storageBytes),
                QuotaOperation.Bandwidth => Interlocked.Read(ref _bandwidthBytes),
                _ => 0
            };
        }

        public QuotaUsage ToUsage()
        {
            return new QuotaUsage
            {
                ProjectId = ProjectId,
                Period = Period,
                ApiRequests = Interlocked.Read(ref _apiRequests),
                DataReads = Interlocked.Read(ref _dataReads),
                DataWrites = Interlocked.Read(ref _dataWrites),
                StorageBytes = Interlocked.Read(ref _storageBytes),
                BandwidthBytes = Interlocked.Read(ref _bandwidthBytes),
                LastUpdated = _lastUpdated
            };
        }
    }
}
