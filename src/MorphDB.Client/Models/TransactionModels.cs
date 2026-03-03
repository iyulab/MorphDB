namespace MorphDB.Client.Models;

/// <summary>
/// Transaction request with atomic operations.
/// </summary>
public sealed class TransactionRequest
{
    /// <summary>
    /// Ordered list of operations to execute atomically.
    /// </summary>
    public required IReadOnlyList<TransactionOperation> Operations { get; init; }
}

/// <summary>
/// A single operation within a transaction.
/// </summary>
public sealed class TransactionOperation
{
    /// <summary>
    /// Optional reference name for cross-operation $ref linking.
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// Target table name.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Operation type: insert, update, delete.
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// Record data for insert/update operations.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Record ID for update/delete operations.
    /// </summary>
    public Guid? Id { get; init; }
}

/// <summary>
/// Transaction execution result.
/// </summary>
public sealed class TransactionResponse
{
    /// <summary>
    /// Whether the entire transaction succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Results for each operation in order.
    /// </summary>
    public IReadOnlyList<TransactionOperationResult> Results { get; init; } = [];

    /// <summary>
    /// Error message if the transaction failed.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Result of a single transaction operation.
/// </summary>
public sealed class TransactionOperationResult
{
    /// <summary>
    /// Reference name if provided.
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// Whether this operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The resulting record data.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Request to finalize draft records.
/// </summary>
public sealed class FinalizeRequest
{
    /// <summary>
    /// Record IDs to finalize.
    /// </summary>
    public IReadOnlyList<Guid>? RecordIds { get; init; }
}

/// <summary>
/// Finalization result.
/// </summary>
public sealed class FinalizeResponse
{
    public bool Success { get; init; }
    public int FinalizedCount { get; init; }
    public int ErrorCount { get; init; }
    public IReadOnlyList<FinalizeError> Errors { get; init; } = [];
}

/// <summary>
/// Error from finalization.
/// </summary>
public sealed class FinalizeError
{
    public Guid RecordId { get; init; }
    public string? Message { get; init; }
}
