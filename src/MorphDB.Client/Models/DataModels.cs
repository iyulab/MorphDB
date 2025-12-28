namespace MorphDB.Client.Models;

/// <summary>
/// Query request with filtering, sorting, and pagination options.
/// </summary>
public sealed class QueryRequest
{
    /// <summary>
    /// Columns to select. If empty, all columns are returned.
    /// </summary>
    public IReadOnlyList<string> Select { get; init; } = [];

    /// <summary>
    /// Filter conditions.
    /// </summary>
    public IReadOnlyList<Filter> Filters { get; init; } = [];

    /// <summary>
    /// Ordering specifications.
    /// </summary>
    public IReadOnlyList<OrderBy> OrderBy { get; init; } = [];

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size. Default is 50.
    /// </summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Filter condition for queries.
/// </summary>
public sealed class Filter
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
    public Filter() { }

    /// <summary>
    /// Creates a new filter with the specified parameters.
    /// </summary>
    public Filter(string column, FilterOperator op, object? value)
    {
        Column = column;
        Operator = op;
        Value = value;
    }
}

/// <summary>
/// Filter operators.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equal to.</summary>
    Equal,

    /// <summary>Not equal to.</summary>
    NotEqual,

    /// <summary>Greater than.</summary>
    GreaterThan,

    /// <summary>Greater than or equal to.</summary>
    GreaterThanOrEqual,

    /// <summary>Less than.</summary>
    LessThan,

    /// <summary>Less than or equal to.</summary>
    LessThanOrEqual,

    /// <summary>Contains substring (case-insensitive).</summary>
    Contains,

    /// <summary>Starts with substring (case-insensitive).</summary>
    StartsWith,

    /// <summary>Ends with substring (case-insensitive).</summary>
    EndsWith,

    /// <summary>Is null.</summary>
    IsNull,

    /// <summary>Is not null.</summary>
    IsNotNull,

    /// <summary>In list.</summary>
    In,

    /// <summary>Not in list.</summary>
    NotIn
}

/// <summary>
/// Ordering specification.
/// </summary>
public sealed class OrderBy
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Sort direction (true for ascending, false for descending).
    /// </summary>
    public bool Ascending { get; init; } = true;

    /// <summary>
    /// Creates a new order by specification.
    /// </summary>
    public OrderBy() { }

    /// <summary>
    /// Creates a new order by specification with the specified parameters.
    /// </summary>
    public OrderBy(string column, bool ascending = true)
    {
        Column = column;
        Ascending = ascending;
    }
}

/// <summary>
/// Paged response wrapper.
/// </summary>
/// <typeparam name="T">Type of items.</typeparam>
public sealed class PagedResponse<T>
{
    /// <summary>
    /// Data items.
    /// </summary>
    public IReadOnlyList<T> Data { get; init; } = [];

    /// <summary>
    /// Pagination information.
    /// </summary>
    public required PaginationInfo Pagination { get; init; }
}

/// <summary>
/// Pagination information.
/// </summary>
public sealed class PaginationInfo
{
    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of items.
    /// </summary>
    public long TotalCount { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage { get; init; }

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage { get; init; }
}

/// <summary>
/// Data record response.
/// </summary>
public sealed class DataRecord
{
    /// <summary>
    /// Record ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Record data as key-value pairs.
    /// </summary>
    public IDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Batch operation request.
/// </summary>
public sealed class BatchRequest
{
    /// <summary>
    /// Records to insert.
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> Inserts { get; init; } = [];

    /// <summary>
    /// Records to update (must include ID).
    /// </summary>
    public IReadOnlyList<IDictionary<string, object?>> Updates { get; init; } = [];

    /// <summary>
    /// Record IDs to delete.
    /// </summary>
    public IReadOnlyList<Guid> Deletes { get; init; } = [];
}

/// <summary>
/// Batch operation response.
/// </summary>
public sealed class BatchResponse
{
    /// <summary>
    /// Inserted record results.
    /// </summary>
    public IReadOnlyList<DataRecord> Inserted { get; init; } = [];

    /// <summary>
    /// Updated record results.
    /// </summary>
    public IReadOnlyList<DataRecord> Updated { get; init; } = [];

    /// <summary>
    /// Number of deleted records.
    /// </summary>
    public int Deleted { get; init; }
}
