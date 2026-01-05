using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for cross-entity transactional operations.
/// All operations within a transaction execute atomically (all succeed or all fail).
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// Executes a transaction containing multiple operations across entities.
    /// Supports $ref references to use results from earlier operations.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="request">The transaction request containing operations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transaction result.</returns>
    Task<TransactionResult> ExecuteAsync(
        Guid tenantId,
        TransactionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a record and updates its row state.
    /// Changes _row_state from 'draft' to 'valid' or 'error'.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="tableName">The logical table name.</param>
    /// <param name="recordId">The record ID to finalize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result with updated row state.</returns>
    Task<FinalizeResult> FinalizeAsync(
        Guid tenantId,
        string tableName,
        Guid recordId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates multiple records and updates their row states.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="tableName">The logical table name.</param>
    /// <param name="recordIds">The record IDs to finalize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation results for each record.</returns>
    Task<IReadOnlyList<FinalizeResult>> FinalizeBatchAsync(
        Guid tenantId,
        string tableName,
        IReadOnlyList<Guid> recordIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a finalize operation.
/// </summary>
public sealed record FinalizeResult
{
    /// <summary>
    /// The record ID that was validated.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Whether the record passed validation.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The new row state after validation.
    /// </summary>
    public RowStateValue NewState { get; init; }

    /// <summary>
    /// Validation errors if the record failed validation.
    /// </summary>
    public IReadOnlyList<RowValidationError> Errors { get; init; } = [];

    /// <summary>
    /// The updated record data (if requested).
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    public static FinalizeResult Valid(Guid recordId, IDictionary<string, object?>? data = null) => new()
    {
        RecordId = recordId,
        Success = true,
        NewState = RowStateValue.Valid,
        Data = data
    };

    public static FinalizeResult Invalid(Guid recordId, IReadOnlyList<RowValidationError> errors, IDictionary<string, object?>? data = null) => new()
    {
        RecordId = recordId,
        Success = false,
        NewState = RowStateValue.Error,
        Errors = errors,
        Data = data
    };

    public static FinalizeResult NotFound(Guid recordId) => new()
    {
        RecordId = recordId,
        Success = false,
        NewState = RowStateValue.Error,
        Errors = [new RowValidationError { Column = "_id", Error = "not_found", Message = "Record not found" }]
    };
}
