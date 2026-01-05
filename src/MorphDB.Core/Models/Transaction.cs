namespace MorphDB.Core.Models;

/// <summary>
/// Represents a cross-entity transaction request containing multiple operations
/// that execute atomically (all succeed or all fail).
/// </summary>
public sealed record TransactionRequest
{
    /// <summary>
    /// List of operations to execute in order within a single transaction.
    /// </summary>
    public required IReadOnlyList<TransactionOperation> Operations { get; init; }

    /// <summary>
    /// Optional: Timeout for the entire transaction in milliseconds.
    /// Default: 30000 (30 seconds).
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// When true, returns full record data for each operation.
    /// When false (default), returns only _id and affected status.
    /// </summary>
    public bool ReturnFullRecords { get; init; }
}

/// <summary>
/// Represents a single operation within a transaction.
/// </summary>
public sealed record TransactionOperation
{
    /// <summary>
    /// The operation type: INSERT, UPDATE, DELETE, or UPSERT.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// The logical table name to operate on.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// The data for INSERT/UPDATE/UPSERT operations.
    /// Can contain $ref references like "$order._id" to reference previous operation results.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// The record ID for UPDATE/DELETE operations.
    /// Can be a GUID or a $ref reference like "$order._id".
    /// </summary>
    public object? Id { get; init; }

    /// <summary>
    /// Optional reference name for this operation's result.
    /// Other operations can reference this using $ref syntax.
    /// Example: "ref": "order" allows "$order._id" references.
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// Key columns for UPSERT operations (determines insert vs update).
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }

    /// <summary>
    /// Write options for this specific operation.
    /// </summary>
    public WriteOptions? Options { get; init; }
}

/// <summary>
/// Result of a transaction execution.
/// </summary>
public sealed record TransactionResult
{
    /// <summary>
    /// Whether the entire transaction succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Results for each operation in order.
    /// </summary>
    public required IReadOnlyList<TransactionOperationResult> Results { get; init; }

    /// <summary>
    /// Error message if the transaction failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Index of the operation that caused the failure (if any).
    /// </summary>
    public int? FailedOperationIndex { get; init; }

    public static TransactionResult Ok(IReadOnlyList<TransactionOperationResult> results) => new()
    {
        Success = true,
        Results = results
    };

    public static TransactionResult Failed(string error, int? failedIndex = null) => new()
    {
        Success = false,
        Results = [],
        Error = error,
        FailedOperationIndex = failedIndex
    };

    public static TransactionResult PartialFailed(
        IReadOnlyList<TransactionOperationResult> results,
        string error,
        int failedIndex) => new()
    {
        Success = false,
        Results = results,
        Error = error,
        FailedOperationIndex = failedIndex
    };
}

/// <summary>
/// Result of a single operation within a transaction.
/// </summary>
public sealed record TransactionOperationResult
{
    /// <summary>
    /// The index of this operation in the request.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Whether this operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The reference name if specified in the request.
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// The record ID (generated or provided).
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// The full record data (if ReturnFullRecords was true).
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Number of rows affected (for UPDATE/DELETE).
    /// </summary>
    public int AffectedRows { get; init; }

    /// <summary>
    /// Error message if this operation failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Validation errors if applicable.
    /// </summary>
    public IReadOnlyList<ValidationError>? ValidationErrors { get; init; }
}

/// <summary>
/// Resolver for $ref references in transaction operations.
/// </summary>
public interface IRefResolver
{
    /// <summary>
    /// Resolves a $ref expression like "$order._id" to its actual value.
    /// </summary>
    /// <param name="refExpression">The reference expression (e.g., "$order._id").</param>
    /// <returns>The resolved value, or null if not found.</returns>
    object? Resolve(string refExpression);

    /// <summary>
    /// Stores a result for later reference.
    /// </summary>
    /// <param name="refName">The reference name (without $ prefix).</param>
    /// <param name="result">The operation result to store.</param>
    void Store(string refName, TransactionOperationResult result);

    /// <summary>
    /// Processes data dictionary, resolving any $ref values.
    /// </summary>
    /// <param name="data">The data to process.</param>
    /// <returns>New dictionary with resolved values.</returns>
    IDictionary<string, object?> ResolveData(IDictionary<string, object?> data);
}
