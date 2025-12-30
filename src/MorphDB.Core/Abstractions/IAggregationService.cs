namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for server-side aggregation queries on dynamic tables.
/// Provides COUNT, SUM, AVG, MIN, MAX with GROUP BY support.
/// </summary>
public interface IAggregationService
{
    /// <summary>
    /// Executes an aggregation query on a table.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="tableName">The logical table name.</param>
    /// <param name="request">The aggregation request with functions and grouping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregation results with grouped data.</returns>
    Task<AggregationResult> AggregateAsync(
        Guid tenantId,
        string tableName,
        AggregationRequest request,
        CancellationToken cancellationToken = default);
}

#region Request and Response Models

/// <summary>
/// Aggregation query request.
/// </summary>
public sealed record AggregationRequest
{
    /// <summary>
    /// The aggregation columns to compute (COUNT, SUM, AVG, MIN, MAX).
    /// </summary>
    public IReadOnlyList<AggregationColumn> Aggregations { get; init; } = [];

    /// <summary>
    /// Columns to group by (logical names).
    /// </summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    /// <summary>
    /// Filter conditions (same format as query filter).
    /// </summary>
    public IReadOnlyList<FilterCondition>? Filter { get; init; }

    /// <summary>
    /// Having conditions (applied after grouping).
    /// </summary>
    public IReadOnlyList<HavingCondition>? Having { get; init; }

    /// <summary>
    /// Order by specifications (can order by aggregation aliases).
    /// </summary>
    public IReadOnlyList<AggregationOrderBy>? OrderBy { get; init; }

    /// <summary>
    /// Maximum number of groups to return.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Number of groups to skip.
    /// </summary>
    public int? Offset { get; init; }
}

/// <summary>
/// Defines an aggregation column to compute.
/// </summary>
public sealed record AggregationColumn
{
    /// <summary>
    /// The aggregation function to apply.
    /// </summary>
    public required AggregateFunction Function { get; init; }

    /// <summary>
    /// The column to aggregate. Null for COUNT(*).
    /// </summary>
    public string? Column { get; init; }

    /// <summary>
    /// The alias for the result column.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Whether to use DISTINCT (e.g., COUNT DISTINCT).
    /// </summary>
    public bool Distinct { get; init; }
}

/// <summary>
/// Filter condition for WHERE clause.
/// </summary>
public sealed record FilterCondition
{
    /// <summary>
    /// The column to filter on (logical name).
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// The filter operator.
    /// </summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public object? Value { get; init; }
}

/// <summary>
/// Having condition for filtering aggregated results.
/// </summary>
public sealed record HavingCondition
{
    /// <summary>
    /// The aggregation alias or function to filter on.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public required object Value { get; init; }
}

/// <summary>
/// Order by specification for aggregation results.
/// </summary>
public sealed record AggregationOrderBy
{
    /// <summary>
    /// Column name or aggregation alias to order by.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Sort direction (ascending or descending).
    /// </summary>
    public bool Descending { get; init; }
}

/// <summary>
/// Aggregation query result.
/// </summary>
public sealed record AggregationResult
{
    /// <summary>
    /// The aggregated data rows.
    /// Each row contains group-by column values and aggregation results.
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> Data { get; init; } = [];

    /// <summary>
    /// Total number of groups (before limit/offset).
    /// Only populated if requested.
    /// </summary>
    public long? TotalGroups { get; init; }

    /// <summary>
    /// Metadata about the aggregation execution.
    /// </summary>
    public AggregationMetadata? Metadata { get; init; }
}

/// <summary>
/// Metadata about the aggregation execution.
/// </summary>
public sealed record AggregationMetadata
{
    /// <summary>
    /// Number of rows scanned.
    /// </summary>
    public long RowsScanned { get; init; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; init; }
}

#endregion
