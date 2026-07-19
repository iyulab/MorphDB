using System.Diagnostics.CodeAnalysis;

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
    [SetsRequiredMembers]
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
    [SetsRequiredMembers]
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
/// The operation kinds a batch entry can carry.
/// </summary>
public static class BatchMethod
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Upsert = "UPSERT";
}

/// <summary>
/// A batch of data operations. Operations run in order and each carries its own table, so one
/// batch may span tables.
/// </summary>
public sealed class BatchRequest
{
    /// <summary>
    /// Operations to execute, in order.
    /// </summary>
    public IReadOnlyList<BatchOperation> Operations { get; init; } = [];
}

/// <summary>
/// A single operation within a <see cref="BatchRequest"/>.
/// </summary>
public sealed class BatchOperation
{
    /// <summary>
    /// One of the <see cref="BatchMethod"/> values.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Table the operation applies to.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Target record, for UPDATE and DELETE.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// Record payload, for INSERT, UPDATE, and UPSERT.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Columns identifying an existing row, for UPSERT.
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Per-operation outcomes of a batch. Operations are reported individually, so a partial failure is
/// visible rather than collapsed into one status.
/// </summary>
public sealed class BatchResponse
{
    /// <summary>
    /// One result per operation, in request order.
    /// </summary>
    public IReadOnlyList<BatchOperationResult> Results { get; init; } = [];

    /// <summary>
    /// Number of operations that succeeded.
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of operations that failed.
    /// </summary>
    public int FailureCount { get; init; }
}

/// <summary>
/// Outcome of one operation in a batch.
/// </summary>
public sealed class BatchOperationResult
{
    /// <summary>
    /// Position of the operation in the request.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Identifying data for the affected record — for inserts, the generated <c>_id</c>.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Failure reason, when <see cref="Success"/> is false.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Rows the operation affected.
    /// </summary>
    public int? AffectedRows { get; init; }
}
