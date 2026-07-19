using System.Diagnostics.CodeAnalysis;

namespace MorphDB.Client.Models;

/// <summary>
/// Aggregation request with GROUP BY support.
/// </summary>
public sealed class AggregationRequest
{
    /// <summary>
    /// Aggregation columns to compute (COUNT, SUM, AVG, MIN, MAX).
    /// </summary>
    public IReadOnlyList<AggregationColumn> Aggregations { get; init; } = [];

    /// <summary>
    /// Columns to group by.
    /// </summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    /// <summary>
    /// Filter conditions (WHERE clause).
    /// </summary>
    public IReadOnlyList<AggregationFilter>? Filter { get; init; }

    /// <summary>
    /// Having conditions (applied after aggregation).
    /// </summary>
    public IReadOnlyList<HavingCondition>? Having { get; init; }

    /// <summary>
    /// Ordering specifications.
    /// </summary>
    public IReadOnlyList<AggregationOrderBy>? OrderBy { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Number of results to skip.
    /// </summary>
    public int? Offset { get; init; }
}

/// <summary>
/// Aggregation column specification.
/// </summary>
public sealed class AggregationColumn
{
    /// <summary>
    /// Aggregate function to apply.
    /// </summary>
    public required AggregateFunction Function { get; init; }

    /// <summary>
    /// Column to aggregate. Required for SUM, AVG, MIN, MAX. Optional for COUNT.
    /// </summary>
    public string? Column { get; init; }

    /// <summary>
    /// Alias for the result column.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Whether to apply DISTINCT (for COUNT).
    /// </summary>
    public bool Distinct { get; init; }

    /// <summary>
    /// Creates a new aggregation column.
    /// </summary>
    public AggregationColumn() { }

    /// <summary>
    /// Creates a COUNT aggregation.
    /// </summary>
    public static AggregationColumn Count(string alias = "count") => new()
    {
        Function = AggregateFunction.Count,
        Alias = alias
    };

    /// <summary>
    /// Creates a COUNT(column) aggregation.
    /// </summary>
    public static AggregationColumn Count(string column, string alias) => new()
    {
        Function = AggregateFunction.Count,
        Column = column,
        Alias = alias
    };

    /// <summary>
    /// Creates a COUNT(DISTINCT column) aggregation.
    /// </summary>
    public static AggregationColumn CountDistinct(string column, string alias) => new()
    {
        Function = AggregateFunction.CountDistinct,
        Column = column,
        Alias = alias
    };

    /// <summary>
    /// Creates a SUM aggregation.
    /// </summary>
    public static AggregationColumn Sum(string column, string alias) => new()
    {
        Function = AggregateFunction.Sum,
        Column = column,
        Alias = alias
    };

    /// <summary>
    /// Creates an AVG aggregation.
    /// </summary>
    public static AggregationColumn Avg(string column, string alias) => new()
    {
        Function = AggregateFunction.Avg,
        Column = column,
        Alias = alias
    };

    /// <summary>
    /// Creates a MIN aggregation.
    /// </summary>
    public static AggregationColumn Min(string column, string alias) => new()
    {
        Function = AggregateFunction.Min,
        Column = column,
        Alias = alias
    };

    /// <summary>
    /// Creates a MAX aggregation.
    /// </summary>
    public static AggregationColumn Max(string column, string alias) => new()
    {
        Function = AggregateFunction.Max,
        Column = column,
        Alias = alias
    };
}

/// <summary>
/// Aggregate functions.
/// </summary>
public enum AggregateFunction
{
    /// <summary>Count rows.</summary>
    Count,

    /// <summary>Count distinct values.</summary>
    CountDistinct,

    /// <summary>Sum values.</summary>
    Sum,

    /// <summary>Average values.</summary>
    Avg,

    /// <summary>Minimum value.</summary>
    Min,

    /// <summary>Maximum value.</summary>
    Max
}

/// <summary>
/// Filter condition for aggregation queries.
/// </summary>
public sealed class AggregationFilter
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Comparison operator.
    /// </summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Creates a new filter.
    /// </summary>
    public AggregationFilter() { }

    /// <summary>
    /// Creates a new filter with the specified parameters.
    /// </summary>
    [SetsRequiredMembers]
    public AggregationFilter(string column, FilterOperator op, object? value)
    {
        Column = column;
        Operator = op;
        Value = value;
    }
}

/// <summary>
/// Having condition for filtering aggregated results.
/// </summary>
public sealed class HavingCondition
{
    /// <summary>
    /// Alias of the aggregation to filter.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Comparison operator.
    /// </summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// Creates a new having condition.
    /// </summary>
    public HavingCondition() { }

    /// <summary>
    /// Creates a new having condition with the specified parameters.
    /// </summary>
    [SetsRequiredMembers]
    public HavingCondition(string alias, FilterOperator op, object value)
    {
        Alias = alias;
        Operator = op;
        Value = value;
    }
}

/// <summary>
/// Ordering specification for aggregation results.
/// </summary>
public sealed class AggregationOrderBy
{
    /// <summary>
    /// Column or aggregation alias to order by.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Sort descending (true) or ascending (false).
    /// </summary>
    public bool Descending { get; init; }

    /// <summary>
    /// Creates a new order by specification.
    /// </summary>
    public AggregationOrderBy() { }

    /// <summary>
    /// Creates a new order by specification with the specified parameters.
    /// </summary>
    [SetsRequiredMembers]
    public AggregationOrderBy(string column, bool descending = false)
    {
        Column = column;
        Descending = descending;
    }
}

/// <summary>
/// Aggregation response.
/// </summary>
public sealed class AggregationResponse
{
    /// <summary>
    /// Aggregated data rows.
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> Data { get; init; } = [];

    /// <summary>
    /// Total number of groups (when using LIMIT/OFFSET).
    /// </summary>
    public long? TotalGroups { get; init; }

    /// <summary>
    /// Aggregation metadata.
    /// </summary>
    public AggregationMetadata? Metadata { get; init; }
}

/// <summary>
/// Metadata about the aggregation operation.
/// </summary>
public sealed class AggregationMetadata
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
