using System.Text.Json;
using MorphDB.Core.Abstractions;

namespace MorphDB.Core.Models;

/// <summary>
/// Metadata for a virtual view (logical table derived from queries).
/// Views provide read-only access to computed/filtered data from base tables.
/// </summary>
public sealed record ViewMetadata
{
    public Guid ViewId { get; init; }
    public Guid ProjectId { get; init; }

    /// <summary>
    /// User-facing name for the view.
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Hash-based physical name (view_{hash}).
    /// </summary>
    public required string PhysicalName { get; init; }

    /// <summary>
    /// View definition containing base tables, joins, columns, and filters.
    /// </summary>
    public required ViewDefinition Definition { get; init; }

    /// <summary>
    /// When true, the view is materialized (cached) for performance.
    /// </summary>
    public bool IsMaterialized { get; init; }

    /// <summary>
    /// Refresh policy for materialized views.
    /// </summary>
    public MaterializedViewRefreshPolicy RefreshPolicy { get; init; } = MaterializedViewRefreshPolicy.OnDemand;

    /// <summary>
    /// Cron expression for scheduled refresh (when RefreshPolicy is Scheduled).
    /// </summary>
    public string? RefreshSchedule { get; init; }

    /// <summary>
    /// Last time the materialized view was refreshed.
    /// </summary>
    public DateTimeOffset? LastRefreshedAt { get; init; }

    /// <summary>
    /// Whether the materialized view data is stale (base tables changed since refresh).
    /// </summary>
    public bool IsStale { get; init; }

    /// <summary>
    /// Additional metadata for the view.
    /// </summary>
    public JsonDocument? Descriptor { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Columns exposed by the view.
    /// </summary>
    public IReadOnlyList<ViewColumnMetadata> Columns { get; init; } = [];
}

/// <summary>
/// Definition of a view's structure and query logic.
/// </summary>
public sealed class ViewDefinition
{
    /// <summary>
    /// Primary base table for the view.
    /// </summary>
    public required string BaseTable { get; init; }

    /// <summary>
    /// Additional tables joined to the base table.
    /// </summary>
    public IReadOnlyList<ViewJoinSpec> Joins { get; init; } = [];

    /// <summary>
    /// Columns to include in the view.
    /// </summary>
    public IReadOnlyList<ViewColumnSpec> Columns { get; init; } = [];

    /// <summary>
    /// Filter conditions applied to the view.
    /// </summary>
    public IReadOnlyList<ViewFilterSpec> Filters { get; init; } = [];

    /// <summary>
    /// Group by columns for aggregate views.
    /// </summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    /// <summary>
    /// Default ordering for view results.
    /// </summary>
    public IReadOnlyList<ViewOrderSpec> OrderBy { get; init; } = [];

    /// <summary>
    /// Maximum number of rows (for limiting view results).
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Whether to include only distinct rows.
    /// </summary>
    public bool Distinct { get; init; }
}

/// <summary>
/// Specification for a join in a view definition.
/// </summary>
public sealed class ViewJoinSpec
{
    /// <summary>
    /// Table to join.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Alias for the joined table.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Type of join.
    /// </summary>
    public ViewJoinType JoinType { get; init; } = ViewJoinType.Left;

    /// <summary>
    /// Join condition (e.g., "orders.customer_id = customers._id").
    /// </summary>
    public required string Condition { get; init; }
}

/// <summary>
/// Specification for a column in a view definition.
/// </summary>
public sealed class ViewColumnSpec
{
    /// <summary>
    /// Source column reference (table.column or just column for base table).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Computed expression (e.g., "price * quantity", "CONCAT(first_name, ' ', last_name)").
    /// Required if Source is null.
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Output column alias (required).
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Data type of the output column (inferred if not specified).
    /// </summary>
    public MorphDataType? DataType { get; init; }

    /// <summary>
    /// Aggregation function for grouped views.
    /// </summary>
    public AggregationFunction? Aggregation { get; init; }
}

/// <summary>
/// Specification for a filter condition in a view definition.
/// </summary>
public sealed class ViewFilterSpec
{
    /// <summary>
    /// Column or expression to filter on.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Comparison operator.
    /// </summary>
    public required FilterOperator Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Logical operator for combining with other filters.
    /// </summary>
    public LogicalOperator LogicalOp { get; init; } = LogicalOperator.And;
}

/// <summary>
/// Specification for ordering in a view definition.
/// </summary>
public sealed class ViewOrderSpec
{
    public required string Column { get; init; }
    public bool Descending { get; init; }
    public NullOrdering NullOrdering { get; init; } = NullOrdering.Last;
}

/// <summary>
/// Metadata for a column exposed by a view.
/// </summary>
public sealed class ViewColumnMetadata
{
    public Guid ColumnId { get; init; }
    public Guid ViewId { get; init; }
    public required string LogicalName { get; init; }
    public required MorphDataType DataType { get; init; }
    public bool IsComputed { get; init; }
    public string? Expression { get; init; }
    public int OrdinalPosition { get; init; }
}

/// <summary>
/// Types of joins supported in views.
/// </summary>
public enum ViewJoinType
{
    Inner,
    Left,
    Right,
    Full,
    Cross
}

/// <summary>
/// Refresh policies for materialized views.
/// </summary>
public enum MaterializedViewRefreshPolicy
{
    /// <summary>
    /// Refresh only when explicitly requested.
    /// </summary>
    OnDemand,

    /// <summary>
    /// Refresh on a schedule (cron).
    /// </summary>
    Scheduled,

    /// <summary>
    /// Refresh incrementally when base tables change (advanced).
    /// </summary>
    Incremental
}

/// <summary>
/// Aggregation functions for view columns.
/// </summary>
public enum AggregationFunction
{
    Count,
    Sum,
    Avg,
    Min,
    Max,
    ArrayAgg,
    StringAgg,
    First,
    Last
}

/// <summary>
/// Logical operators for combining filters.
/// </summary>
public enum LogicalOperator
{
    And,
    Or
}

/// <summary>
/// Null ordering for sort operations.
/// </summary>
public enum NullOrdering
{
    First,
    Last
}
