namespace MorphDB.Core.Abstractions;

/// <summary>
/// Constants for query limits.
/// </summary>
public static class QueryLimits
{
    /// <summary>
    /// Maximum allowed page size for any query. Values exceeding this are clamped.
    /// </summary>
    public const int MaxPageSize = 10000;
}

/// <summary>
/// Fluent query builder for MorphDB queries using logical names.
/// </summary>
public interface IMorphQueryBuilder
{
    /// <summary>
    /// Starts a query from a table.
    /// </summary>
    IMorphQuery From(string tableName);

    /// <summary>
    /// Starts a query from a table with an alias.
    /// </summary>
    IMorphQuery From(string tableName, string tableAlias);
}

/// <summary>
/// Represents a query that can be built and executed.
/// </summary>
public interface IMorphQuery
{
    /// <summary>
    /// Selects specific columns.
    /// </summary>
    IMorphQuery SelectColumns(params string[] columns);

    /// <summary>
    /// Selects all columns.
    /// </summary>
    IMorphQuery SelectAll();

    /// <summary>
    /// Reads the rows a soft delete has marked as well. Without it, a read of a table created with
    /// soft delete sees only rows whose <c>_deleted_at</c> is null — lists, single reads, counts and
    /// aggregates alike. No effect on a table without soft delete.
    /// </summary>
    IMorphQuery IncludeDeleted();

    /// <summary>
    /// Adds a WHERE condition. <see cref="FilterOperator.IsNull"/> and <see cref="FilterOperator.IsNotNull"/>
    /// ignore <paramref name="value"/>. <see cref="FilterOperator.In"/> and <see cref="FilterOperator.NotIn"/>
    /// take a list of values (a single value is refused with <see cref="ArgumentException"/>);
    /// <see cref="FilterOperator.Between"/> is refused with <see cref="NotSupportedException"/>.
    /// </summary>
    IMorphQuery Where(string column, FilterOperator op, object? value);

    /// <summary>
    /// Adds a WHERE condition with AND.
    /// </summary>
    IMorphQuery AndWhere(string column, FilterOperator op, object? value);

    /// <summary>
    /// Adds a WHERE condition with OR.
    /// </summary>
    IMorphQuery OrWhere(string column, FilterOperator op, object? value);

    /// <summary>
    /// Adds the conditions <paramref name="group"/> adds as one parenthesized condition, joined with AND —
    /// <c>a AND (b OR c)</c>. Inside, the first condition opens the group and each <see cref="OrWhere"/> or
    /// <see cref="AndWhere"/> joins the next; groups nest. <paramref name="group"/> adds conditions only.
    /// </summary>
    IMorphQuery WhereGroup(Func<IMorphQuery, IMorphQuery> group);

    /// <summary>
    /// <see cref="WhereGroup"/> joined with OR — <c>a OR (b AND c)</c>.
    /// </summary>
    IMorphQuery OrWhereGroup(Func<IMorphQuery, IMorphQuery> group);

    /// <summary>
    /// Adds a WHERE IN condition.
    /// </summary>
    IMorphQuery WhereIn(string column, IEnumerable<object> values);

    /// <summary>
    /// Adds a WHERE NOT IN condition.
    /// </summary>
    IMorphQuery WhereNotIn(string column, IEnumerable<object> values);

    /// <summary>
    /// Adds a WHERE NULL condition.
    /// </summary>
    IMorphQuery WhereNull(string column);

    /// <summary>
    /// Adds a WHERE NOT NULL condition.
    /// </summary>
    IMorphQuery WhereNotNull(string column);

    /// <summary>
    /// Joins another table.
    /// </summary>
    IMorphQuery Join(string tableName, string sourceColumn, string targetColumn);

    /// <summary>
    /// Left joins another table.
    /// </summary>
    IMorphQuery LeftJoin(string tableName, string sourceColumn, string targetColumn);

    /// <summary>
    /// Orders by a column ascending.
    /// </summary>
    IMorphQuery OrderBy(string column);

    /// <summary>
    /// Orders by a column descending.
    /// </summary>
    IMorphQuery OrderByDesc(string column);

    /// <summary>
    /// Groups by columns.
    /// </summary>
    IMorphQuery GroupBy(params string[] columns);

    /// <summary>
    /// Adds a HAVING condition.
    /// </summary>
    IMorphQuery Having(string column, FilterOperator op, object? value);

    /// <summary>
    /// Limits the number of results.
    /// </summary>
    IMorphQuery Limit(int count);

    /// <summary>
    /// Skips a number of results.
    /// </summary>
    IMorphQuery Offset(int count);

    /// <summary>
    /// Executes the query and returns results.
    /// </summary>
    Task<IReadOnlyList<IDictionary<string, object?>>> ToListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the query and returns the first result or null.
    /// </summary>
    Task<IDictionary<string, object?>?> FirstOrDefaultAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a count query.
    /// </summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a SUM aggregate query.
    /// </summary>
    Task<decimal?> SumAsync(string column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an AVG aggregate query.
    /// </summary>
    Task<decimal?> AvgAsync(string column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a MIN aggregate query.
    /// </summary>
    Task<T?> MinAsync<T>(string column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a MAX aggregate query.
    /// </summary>
    Task<T?> MaxAsync<T>(string column, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects aggregate columns (for GROUP BY queries).
    /// </summary>
    IMorphQuery SelectAggregate(AggregateFunction aggregateFunction, string columnName, string? resultAlias = null);

    /// <summary>
    /// Gets the generated SQL for debugging.
    /// </summary>
    string ToSql();

    /// <summary>
    /// Gets the query parameters for debugging.
    /// </summary>
    IDictionary<string, object?> GetParameters();

    /// <summary>
    /// Gets the physical WHERE clause SQL and parameters for batch operations.
    /// Returns the WHERE clause without the "WHERE" keyword.
    /// </summary>
    Task<(string WhereSql, IDictionary<string, object?> Parameters)> GetPhysicalWhereClauseAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter operators for WHERE conditions.
/// </summary>
public enum FilterOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEquals,
    LessThan,
    LessThanOrEquals,
    Like,
    NotLike,
    ILike,
    Contains,
    StartsWith,
    EndsWith,
    In,
    NotIn,
    IsNull,
    IsNotNull,
    Between,

    /// <summary>
    /// The row has at least one related row: a row of another table whose column holds this row's value of
    /// the condition's column, and that the related conditions keep — <c>EXISTS</c>, as OData's <c>any()</c>
    /// and PostgREST's embedded filters ask. The value is a <see cref="RelatedRows"/>.
    /// </summary>
    Exists
}

/// <summary>
/// The value of an <see cref="FilterOperator.Exists"/> condition: the rows of <paramref name="Table"/> whose
/// <paramref name="Column"/> holds the filtered row's value, kept by every one of <paramref name="Where"/>.
/// The related table is read as any read of it is — soft-deleted rows left out, its row-level security applied.
/// </summary>
/// <param name="Table">The related table.</param>
/// <param name="Column">The related table's column that holds the filtered row's value.</param>
/// <param name="Where">Conditions on the related rows' stored columns; an <see cref="FilterOperator.Exists"/> among them is refused.</param>
public sealed record RelatedRows(string Table, string Column, IReadOnlyList<FilterCondition> Where);

/// <summary>
/// Aggregate functions for SELECT and queries.
/// </summary>
public enum AggregateFunction
{
    Count,
    Sum,
    Avg,
    Min,
    Max,
    CountDistinct,

    /// <summary>
    /// The group's values of a column as one array, in ascending order of the value — what a count is
    /// made of, read in the same statement as the count. <see cref="AggregationColumn.Limit"/> keeps the
    /// first values only; the group's full size is a count beside it.
    /// </summary>
    ArrayAgg
}
