namespace MorphDB.Core.Abstractions;

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
    /// Adds a WHERE condition.
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
    /// Uses cursor-based pagination.
    /// </summary>
    /// <param name="cursorColumn">Column to use for cursor (typically 'id' or 'created_at').</param>
    /// <param name="cursorValue">The cursor value to start after.</param>
    /// <param name="pageSize">Number of items per page.</param>
    IMorphQuery After(string cursorColumn, object cursorValue, int pageSize);

    /// <summary>
    /// Uses cursor-based pagination (before cursor).
    /// </summary>
    IMorphQuery Before(string cursorColumn, object cursorValue, int pageSize);

    /// <summary>
    /// Gets the generated SQL for debugging.
    /// </summary>
    string ToSql();

    /// <summary>
    /// Gets the query parameters for debugging.
    /// </summary>
    IDictionary<string, object?> GetParameters();
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
    EndsWith
}

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
    CountDistinct
}
