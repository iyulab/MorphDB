namespace MorphDB.Client.Models;

/// <summary>
/// View information response.
/// </summary>
public sealed class ViewInfo
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string BaseTable { get; init; }
    public IReadOnlyList<ViewColumnInfo> Columns { get; init; } = [];
    public IReadOnlyList<ViewJoin>? Joins { get; init; }
    public IReadOnlyList<ViewFilter>? Filters { get; init; }
    public IReadOnlyList<string>? GroupBy { get; init; }
    public IReadOnlyList<ViewOrder>? OrderBy { get; init; }
    public int? Limit { get; init; }
    public bool Distinct { get; init; }
    public bool IsMaterialized { get; init; }

    /// <summary>For a materialized view: <c>OnDemand</c>, <c>Scheduled</c> or <c>Incremental</c>.</summary>
    public string? RefreshPolicy { get; init; }

    /// <summary>For a <c>Scheduled</c> refresh policy, its cron expression.</summary>
    public string? RefreshSchedule { get; init; }
    public DateTimeOffset? LastRefreshedAt { get; init; }
    public bool IsStale { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// View column information.
/// </summary>
public sealed class ViewColumnInfo
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsComputed { get; init; }
    public string? Expression { get; init; }
}

/// <summary>
/// Request to create a view — the shape the service reads, member for member; it refuses a member
/// it does not know.
/// </summary>
public sealed class CreateViewRequest
{
    public required string Name { get; init; }

    /// <summary>The table the view reads from.</summary>
    public required string BaseTable { get; init; }

    /// <summary>The columns the view exposes.</summary>
    public IReadOnlyList<ViewColumn> Columns { get; init; } = [];

    /// <summary>Further tables joined to the base table.</summary>
    public IReadOnlyList<ViewJoin>? Joins { get; init; }

    /// <summary>Conditions every row of the view meets.</summary>
    public IReadOnlyList<ViewFilter>? Filters { get; init; }

    /// <summary>Columns an aggregate view groups by.</summary>
    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>The order the view's rows are read in.</summary>
    public IReadOnlyList<ViewOrder>? OrderBy { get; init; }

    /// <summary>The most rows the view holds.</summary>
    public int? Limit { get; init; }

    /// <summary>Whether the view holds distinct rows only.</summary>
    public bool Distinct { get; init; }

    /// <summary>Creates a materialized view, read from its last refresh.</summary>
    public bool Materialized { get; init; }

    /// <summary>For a materialized view: <c>OnDemand</c> (the default), <c>Scheduled</c> or <c>Incremental</c>.</summary>
    public string? RefreshPolicy { get; init; }

    /// <summary>For a <c>Scheduled</c> refresh policy, its cron expression.</summary>
    public string? RefreshSchedule { get; init; }

    public string? Description { get; init; }
}

/// <summary>
/// One column of a view: a source column or an expression, under the name the view gives it.
/// </summary>
public sealed class ViewColumn
{
    /// <summary>The source column (<c>table.column</c> when the view joins), or null for an expression.</summary>
    public string? Source { get; init; }

    /// <summary>A computed expression, used instead of <see cref="Source"/>.</summary>
    public string? Expression { get; init; }

    /// <summary>The column's name in the view.</summary>
    public required string Alias { get; init; }

    /// <summary>The column's type, for an expression.</summary>
    public string? DataType { get; init; }

    /// <summary>
    /// An aggregate over the source: <c>count</c>, <c>sum</c>, <c>avg</c>, <c>min</c>, <c>max</c>, <c>array_agg</c>,
    /// <c>string_agg</c>, <c>first</c> or <c>last</c>.
    /// </summary>
    public string? Aggregation { get; init; }
}

/// <summary>
/// A table joined to a view's base table.
/// </summary>
public sealed class ViewJoin
{
    public required string Table { get; init; }
    public string? Alias { get; init; }

    /// <summary><c>Inner</c>, <c>Left</c> (the default), <c>Right</c>, <c>Full</c> or <c>Cross</c>.</summary>
    public string JoinType { get; init; } = "Left";

    /// <summary>The join condition, e.g. <c>orders.customer_id = customers.id</c>.</summary>
    public required string Condition { get; init; }
}

/// <summary>
/// A condition the rows of a view meet.
/// </summary>
public sealed class ViewFilter
{
    public required string Field { get; init; }

    /// <summary>
    /// <c>eq</c>, <c>neq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>, <c>like</c>, <c>ilike</c>, <c>in</c>,
    /// <c>notin</c>, <c>isnull</c>, <c>isnotnull</c>, <c>between</c>, <c>contains</c>, <c>startswith</c> or <c>endswith</c>.
    /// </summary>
    public required string Operator { get; init; }

    public object? Value { get; init; }

    /// <summary>How this condition combines with the one before it: <c>And</c> (the default) or <c>Or</c>.</summary>
    public string LogicalOp { get; init; } = "And";
}

/// <summary>
/// One key of the order a view's rows are read in.
/// </summary>
public sealed class ViewOrder
{
    public required string Column { get; init; }
    public bool Descending { get; init; }

    /// <summary><c>First</c> or <c>Last</c> (the default).</summary>
    public string NullOrdering { get; init; } = "Last";
}

/// <summary>
/// Rows read from a view.
/// </summary>
public sealed class ViewRows
{
    /// <summary>The rows, column name to value.</summary>
    public IReadOnlyList<IDictionary<string, object?>> Data { get; init; } = [];

    /// <summary>How many rows the view holds under the query's filters.</summary>
    public long TotalCount { get; init; }

    /// <summary>Whether rows remain past this page.</summary>
    public bool HasMore { get; init; }
}

/// <summary>
/// Changes to a view (mirrors the server's <c>UpdateViewApiRequest</c>). Members left null keep the
/// view's current definition.
/// </summary>
public sealed class UpdateViewRequest
{
    public string? Name { get; init; }
    public IReadOnlyList<ViewColumn>? Columns { get; init; }
    public IReadOnlyList<ViewJoin>? Joins { get; init; }
    public IReadOnlyList<ViewFilter>? Filters { get; init; }
    public IReadOnlyList<string>? GroupBy { get; init; }
    public IReadOnlyList<ViewOrder>? OrderBy { get; init; }
    public int? Limit { get; init; }
    public bool? Distinct { get; init; }

    /// <summary>For a materialized view: <c>OnDemand</c>, <c>Scheduled</c> or <c>Incremental</c>.</summary>
    public string? RefreshPolicy { get; init; }

    /// <summary>For a <c>Scheduled</c> refresh policy, its cron expression.</summary>
    public string? RefreshSchedule { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Whether a materialized view is behind its base tables.
/// </summary>
public sealed class ViewStaleness
{
    public bool IsStale { get; init; }
    public DateTimeOffset? LastRefreshedAt { get; init; }
}
