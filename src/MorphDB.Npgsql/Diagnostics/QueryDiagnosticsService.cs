using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Diagnostics;

namespace MorphDB.Npgsql.Diagnostics;

/// <summary>
/// In-memory query diagnostics service for tracking query performance.
/// Detects slow queries and provides statistics for monitoring.
/// </summary>
public sealed partial class QueryDiagnosticsService : IQueryDiagnostics
{
    private readonly QueryDiagnosticsOptions _options;
    private readonly ILogger<QueryDiagnosticsService> _logger;
    private readonly ConcurrentQueue<QueryExecutionEntry> _slowQueries = new();
    private readonly ConcurrentDictionary<QueryOperationType, OperationAccumulator> _operationStats = new();
    private DateTimeOffset _statisticsStartTime = DateTimeOffset.UtcNow;
    private long _totalQueries;
    private long _slowQueryCount;
    private long _failedQueryCount;

    public QueryDiagnosticsService(
        IOptions<QueryDiagnosticsOptions> options,
        ILogger<QueryDiagnosticsService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public TimeSpan SlowQueryThreshold => TimeSpan.FromMilliseconds(_options.SlowQueryThresholdMs);

    public void RecordQuery(QueryExecutionEntry entry)
    {
        if (!_options.Enabled)
            return;

        Interlocked.Increment(ref _totalQueries);

        if (entry.IsSlow)
        {
            Interlocked.Increment(ref _slowQueryCount);
            EnqueueSlowQuery(entry);

            if (_options.LogSlowQueries)
            {
                LogSlowQuery(entry);
            }
        }

        if (entry.ErrorMessage is not null)
        {
            Interlocked.Increment(ref _failedQueryCount);
        }

        // Update operation statistics
        _operationStats.AddOrUpdate(
            entry.OperationType,
            _ => new OperationAccumulator
            {
                Count = 1,
                TotalDurationMs = entry.DurationMs,
                TotalRows = entry.RowCount,
                MaxDurationMs = entry.DurationMs,
                Durations = [entry.DurationMs]
            },
            (_, acc) =>
            {
                acc.Count++;
                acc.TotalDurationMs += entry.DurationMs;
                acc.TotalRows += entry.RowCount;
                if (entry.DurationMs > acc.MaxDurationMs)
                    acc.MaxDurationMs = entry.DurationMs;
                lock (acc.Durations)
                {
                    acc.Durations.Add(entry.DurationMs);
                    // Keep only last 10000 for percentile calculation
                    if (acc.Durations.Count > 10000)
                        acc.Durations.RemoveAt(0);
                }
                return acc;
            });
    }

    private void EnqueueSlowQuery(QueryExecutionEntry entry)
    {
        _slowQueries.Enqueue(entry);

        // Trim if exceeds max entries
        while (_slowQueries.Count > _options.MaxSlowQueryEntries)
        {
            _slowQueries.TryDequeue(out _);
        }
    }

    private void LogSlowQuery(QueryExecutionEntry entry)
    {
        if (_options.IncludeQueryPatterns && entry.QueryPattern is not null)
        {
            LogSlowQueryWithPattern(
                entry.ExecutionId,
                entry.DurationMs,
                _options.SlowQueryThresholdMs,
                entry.OperationType,
                entry.TableName ?? "unknown",
                entry.RowCount,
                entry.QueryPattern);
        }
        else
        {
            LogSlowQueryBasic(
                entry.ExecutionId,
                entry.DurationMs,
                _options.SlowQueryThresholdMs,
                entry.OperationType,
                entry.TableName ?? "unknown",
                entry.RowCount);
        }
    }

    public IReadOnlyList<QueryExecutionEntry> GetRecentSlowQueries(int count = 100)
    {
        return _slowQueries
            .Reverse()
            .Take(count)
            .ToList();
    }

    public QueryStatistics GetStatistics(DateTimeOffset? since = null)
    {
        var periodStart = since ?? _statisticsStartTime;
        var allDurations = new List<long>();

        var byOperationType = new Dictionary<QueryOperationType, OperationStatistics>();

        foreach (var (opType, acc) in _operationStats)
        {
            byOperationType[opType] = new OperationStatistics
            {
                Count = acc.Count,
                AverageDurationMs = acc.Count > 0 ? (double)acc.TotalDurationMs / acc.Count : 0,
                TotalRowsAffected = acc.TotalRows
            };

            lock (acc.Durations)
            {
                allDurations.AddRange(acc.Durations);
            }
        }

        var (p95, p99, max) = CalculatePercentiles(allDurations);

        return new QueryStatistics
        {
            TotalQueries = Interlocked.Read(ref _totalQueries),
            SlowQueries = Interlocked.Read(ref _slowQueryCount),
            FailedQueries = Interlocked.Read(ref _failedQueryCount),
            AverageDurationMs = allDurations.Count > 0 ? allDurations.Average() : 0,
            P95DurationMs = p95,
            P99DurationMs = p99,
            MaxDurationMs = max,
            ByOperationType = byOperationType,
            PeriodStart = periodStart,
            PeriodEnd = DateTimeOffset.UtcNow
        };
    }

    private static (double p95, double p99, long max) CalculatePercentiles(List<long> durations)
    {
        if (durations.Count == 0)
            return (0, 0, 0);

        var sorted = durations.OrderBy(d => d).ToList();
        var p95Index = (int)(sorted.Count * 0.95);
        var p99Index = (int)(sorted.Count * 0.99);

        return (
            sorted[Math.Min(p95Index, sorted.Count - 1)],
            sorted[Math.Min(p99Index, sorted.Count - 1)],
            sorted[^1]
        );
    }

    public void ClearStatistics()
    {
        _slowQueries.Clear();
        _operationStats.Clear();
        Interlocked.Exchange(ref _totalQueries, 0);
        Interlocked.Exchange(ref _slowQueryCount, 0);
        Interlocked.Exchange(ref _failedQueryCount, 0);
        _statisticsStartTime = DateTimeOffset.UtcNow;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Slow query detected [ExecutionId={ExecutionId}]: {DurationMs}ms (threshold: {ThresholdMs}ms) - {OperationType} on {TableName}, {RowCount} rows")]
    private partial void LogSlowQueryBasic(
        Guid executionId,
        long durationMs,
        int thresholdMs,
        QueryOperationType operationType,
        string tableName,
        int rowCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Slow query detected [ExecutionId={ExecutionId}]: {DurationMs}ms (threshold: {ThresholdMs}ms) - {OperationType} on {TableName}, {RowCount} rows, Pattern: {QueryPattern}")]
    private partial void LogSlowQueryWithPattern(
        Guid executionId,
        long durationMs,
        int thresholdMs,
        QueryOperationType operationType,
        string tableName,
        int rowCount,
        string queryPattern);

    private sealed class OperationAccumulator
    {
        public long Count;
        public long TotalDurationMs;
        public long TotalRows;
        public long MaxDurationMs;
        public List<long> Durations = [];
    }
}

/// <summary>
/// Helper for measuring query execution time.
/// </summary>
public sealed class QueryExecutionScope : IDisposable
{
    private readonly IQueryDiagnostics _diagnostics;
    private readonly Stopwatch _stopwatch;
    private readonly Guid _executionId;
    private readonly Guid? _tenantId;
    private readonly string? _tableName;
    private readonly QueryOperationType _operationType;
    private readonly string? _source;
    private string? _queryPattern;
    private int _rowCount;
    private string? _errorMessage;
    private bool _disposed;

    public QueryExecutionScope(
        IQueryDiagnostics diagnostics,
        Guid? tenantId,
        string? tableName,
        QueryOperationType operationType,
        string? source = null)
    {
        _diagnostics = diagnostics;
        _tenantId = tenantId;
        _tableName = tableName;
        _operationType = operationType;
        _source = source;
        _executionId = Guid.NewGuid();
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Sets the number of rows affected by the query.
    /// </summary>
    public void SetRowCount(int rowCount) => _rowCount = rowCount;

    /// <summary>
    /// Sets the query pattern (normalized query without literals).
    /// </summary>
    public void SetQueryPattern(string pattern) => _queryPattern = pattern;

    /// <summary>
    /// Sets an error message if the query failed.
    /// </summary>
    public void SetError(string message) => _errorMessage = message;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stopwatch.Stop();

        var durationMs = _stopwatch.ElapsedMilliseconds;
        var isSlow = durationMs >= _diagnostics.SlowQueryThreshold.TotalMilliseconds;

        _diagnostics.RecordQuery(new QueryExecutionEntry
        {
            ExecutionId = _executionId,
            TenantId = _tenantId,
            TableName = _tableName,
            OperationType = _operationType,
            QueryPattern = _queryPattern,
            RowCount = _rowCount,
            DurationMs = durationMs,
            IsSlow = isSlow,
            ExecutedAt = DateTimeOffset.UtcNow,
            ErrorMessage = _errorMessage,
            Source = _source
        });
    }
}
