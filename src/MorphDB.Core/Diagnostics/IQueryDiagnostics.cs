namespace MorphDB.Core.Diagnostics;

/// <summary>
/// Interface for query diagnostics and performance monitoring.
/// Supports slow query detection, query metrics, and performance analysis.
/// </summary>
public interface IQueryDiagnostics
{
    /// <summary>
    /// Records a query execution with timing information.
    /// </summary>
    /// <param name="entry">The query execution entry.</param>
    void RecordQuery(QueryExecutionEntry entry);

    /// <summary>
    /// Gets the slow query threshold in milliseconds.
    /// </summary>
    TimeSpan SlowQueryThreshold { get; }

    /// <summary>
    /// Gets recent slow queries for diagnostics.
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Recent slow query entries.</returns>
    IReadOnlyList<QueryExecutionEntry> GetRecentSlowQueries(int count = 100);

    /// <summary>
    /// Gets query statistics for a time period.
    /// </summary>
    /// <param name="since">Start of the time period.</param>
    /// <returns>Aggregated query statistics.</returns>
    QueryStatistics GetStatistics(DateTimeOffset? since = null);

    /// <summary>
    /// Clears accumulated query statistics.
    /// </summary>
    void ClearStatistics();
}

/// <summary>
/// Represents a single query execution entry.
/// </summary>
public sealed record QueryExecutionEntry
{
    /// <summary>
    /// Unique identifier for this query execution.
    /// </summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>
    /// The project context for this query.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// The logical table name being queried.
    /// </summary>
    public string? TableName { get; init; }

    /// <summary>
    /// The type of operation (SELECT, INSERT, UPDATE, DELETE).
    /// </summary>
    public required QueryOperationType OperationType { get; init; }

    /// <summary>
    /// Normalized query pattern (without literal values).
    /// </summary>
    public string? QueryPattern { get; init; }

    /// <summary>
    /// Number of rows affected or returned.
    /// </summary>
    public int RowCount { get; init; }

    /// <summary>
    /// Query execution duration in milliseconds.
    /// </summary>
    public required long DurationMs { get; init; }

    /// <summary>
    /// Whether this query exceeded the slow query threshold.
    /// </summary>
    public required bool IsSlow { get; init; }

    /// <summary>
    /// Timestamp when the query was executed.
    /// </summary>
    public required DateTimeOffset ExecutedAt { get; init; }

    /// <summary>
    /// Optional error message if the query failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Source of the query (API endpoint, GraphQL, OData, etc.).
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// Types of query operations.
/// </summary>
public enum QueryOperationType
{
    Select,
    Insert,
    Update,
    Delete,
    Upsert,
    BatchInsert,
    BatchUpdate,
    BatchDelete,
    Aggregate,
    Schema,
    Other
}

/// <summary>
/// Aggregated query statistics.
/// </summary>
public sealed record QueryStatistics
{
    /// <summary>
    /// Total number of queries executed.
    /// </summary>
    public long TotalQueries { get; init; }

    /// <summary>
    /// Number of slow queries detected.
    /// </summary>
    public long SlowQueries { get; init; }

    /// <summary>
    /// Number of failed queries.
    /// </summary>
    public long FailedQueries { get; init; }

    /// <summary>
    /// Average query duration in milliseconds.
    /// </summary>
    public double AverageDurationMs { get; init; }

    /// <summary>
    /// 95th percentile query duration in milliseconds.
    /// </summary>
    public double P95DurationMs { get; init; }

    /// <summary>
    /// 99th percentile query duration in milliseconds.
    /// </summary>
    public double P99DurationMs { get; init; }

    /// <summary>
    /// Maximum query duration in milliseconds.
    /// </summary>
    public long MaxDurationMs { get; init; }

    /// <summary>
    /// Breakdown by operation type.
    /// </summary>
    public IReadOnlyDictionary<QueryOperationType, OperationStatistics> ByOperationType { get; init; }
        = new Dictionary<QueryOperationType, OperationStatistics>();

    /// <summary>
    /// Start of the statistics period.
    /// </summary>
    public DateTimeOffset PeriodStart { get; init; }

    /// <summary>
    /// End of the statistics period.
    /// </summary>
    public DateTimeOffset PeriodEnd { get; init; }
}

/// <summary>
/// Statistics for a specific operation type.
/// </summary>
public sealed record OperationStatistics
{
    public long Count { get; init; }
    public double AverageDurationMs { get; init; }
    public long TotalRowsAffected { get; init; }
}

/// <summary>
/// Configuration options for query diagnostics.
/// </summary>
public sealed class QueryDiagnosticsOptions
{
    /// <summary>
    /// Whether query diagnostics is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Threshold in milliseconds for slow query detection.
    /// Default is 1000ms (1 second).
    /// </summary>
    public int SlowQueryThresholdMs { get; set; } = 1000;

    /// <summary>
    /// Maximum number of slow queries to retain in memory.
    /// </summary>
    public int MaxSlowQueryEntries { get; set; } = 1000;

    /// <summary>
    /// Whether to log slow queries via ILogger.
    /// </summary>
    public bool LogSlowQueries { get; set; } = true;

    /// <summary>
    /// Whether to include query patterns in logs (may contain sensitive info).
    /// </summary>
    public bool IncludeQueryPatterns { get; set; }

    /// <summary>
    /// Duration after which statistics are automatically cleared.
    /// Default is 24 hours.
    /// </summary>
    public TimeSpan StatisticsRetentionPeriod { get; set; } = TimeSpan.FromHours(24);
}
