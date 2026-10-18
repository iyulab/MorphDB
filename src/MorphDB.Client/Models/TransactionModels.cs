namespace MorphDB.Client.Models;

/// <summary>
/// Transaction request with atomic operations — the shape the service reads, member for member; it
/// refuses a member it does not know.
/// </summary>
public sealed class TransactionRequest
{
    /// <summary>
    /// Ordered list of operations to execute atomically.
    /// </summary>
    public required IReadOnlyList<TransactionOperation> Operations { get; init; }

    /// <summary>
    /// How long the transaction may run, in milliseconds; the service's default when null.
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// Whether each result carries the full record rather than its id.
    /// </summary>
    public bool ReturnFullRecords { get; init; }
}

/// <summary>
/// A single operation within a transaction.
/// </summary>
public sealed class TransactionOperation
{
    /// <summary>
    /// One of the <see cref="BatchMethod"/> values.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Target table name.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Record data for insert, update and upsert. A value may be a reference to an earlier
    /// operation's result, such as <c>$order._id</c>.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Record ID for update and delete: a GUID, or a reference such as <c>$order._id</c>.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Name later operations reference this operation's result by (<c>$name.property</c>).
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// The columns an upsert matches an existing record on.
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }

    /// <summary>
    /// <c>default</c>, or <c>draft</c> to write without validation until the record is finalized.
    /// </summary>
    public string? Mode { get; init; }
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

    /// <summary>
    /// The index of the operation that failed, when one did.
    /// </summary>
    public int? FailedOperationIndex { get; init; }
}

/// <summary>
/// Result of a single transaction operation.
/// </summary>
public sealed class TransactionOperationResult
{
    /// <summary>
    /// The operation's position in the request.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Whether this operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Reference name if provided.
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// The record the operation wrote.
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// The resulting record data, when the request asked for full records.
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Rows the operation changed.
    /// </summary>
    public int AffectedRows { get; init; }

    /// <summary>
    /// Why the operation failed, when it did.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The fields that failed validation, when that is why.
    /// </summary>
    public IReadOnlyList<TransactionValidationError>? ValidationErrors { get; init; }
}

/// <summary>
/// A field that failed validation in a transaction operation.
/// </summary>
public sealed class TransactionValidationError
{
    public required string Field { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
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
    /// <summary>
    /// One result per record asked for.
    /// </summary>
    public IReadOnlyList<FinalizeResult> Results { get; init; } = [];

    /// <summary>
    /// Records that passed validation and are now valid.
    /// </summary>
    public int ValidCount { get; init; }

    /// <summary>
    /// Records that failed validation and stay drafts.
    /// </summary>
    public int ErrorCount { get; init; }
}

/// <summary>
/// The finalization of one record.
/// </summary>
public sealed class FinalizeResult
{
    public Guid RecordId { get; init; }
    public bool Success { get; init; }

    /// <summary>
    /// The record's state afterwards: <c>valid</c>, or <c>draft</c> when it failed validation.
    /// </summary>
    public string NewState { get; init; } = "valid";

    /// <summary>
    /// The columns that failed validation, when the record did.
    /// </summary>
    public IReadOnlyList<FinalizeError>? Errors { get; init; }

    public IDictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// A column that failed validation when a record was finalized.
/// </summary>
public sealed class FinalizeError
{
    public required string Column { get; init; }

    /// <summary>The rule that failed, as a code.</summary>
    public required string Error { get; init; }
    public required string Message { get; init; }
    public object? AttemptedValue { get; init; }
}
