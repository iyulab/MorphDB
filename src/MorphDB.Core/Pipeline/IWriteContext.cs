using MorphDB.Core.Models;
using MorphDB.Core.Security;

namespace MorphDB.Core.Pipeline;

/// <summary>
/// Context for a write operation, passed through the pipeline.
/// </summary>
public interface IWriteContext
{
    /// <summary>
    /// The project ID for this operation.
    /// </summary>
    Guid ProjectId { get; }

    /// <summary>
    /// The table metadata.
    /// </summary>
    TableMetadata Table { get; }

    /// <summary>
    /// The operation type (Insert, Update, Delete).
    /// </summary>
    WriteOperationType OperationType { get; }

    /// <summary>
    /// The record ID (for Update/Delete operations).
    /// </summary>
    Guid? RecordId { get; }

    /// <summary>
    /// The data being written (mutable during pipeline).
    /// </summary>
    IDictionary<string, object?> Data { get; }

    /// <summary>
    /// Original data before transformations (for audit).
    /// </summary>
    IDictionary<string, object?> OriginalData { get; }

    /// <summary>
    /// Existing record data (for Update operations).
    /// </summary>
    IDictionary<string, object?>? ExistingData { get; }

    /// <summary>
    /// Logical key columns for Upsert conflict resolution. Null means the primary key.
    /// </summary>
    IReadOnlyList<string>? KeyColumns { get; }

    /// <summary>
    /// Write options controlling validation behavior.
    /// </summary>
    WriteOptions Options { get; }

    /// <summary>
    /// Security context for the current user.
    /// </summary>
    SecurityContext? SecurityContext { get; }

    /// <summary>
    /// Validation errors collected during pipeline.
    /// </summary>
    IList<ValidationError> Errors { get; }

    /// <summary>
    /// Whether the pipeline should continue (false if critical error).
    /// </summary>
    bool ShouldContinue { get; set; }

    /// <summary>
    /// Cancellation token for the operation.
    /// </summary>
    CancellationToken CancellationToken { get; }
}

/// <summary>
/// Type of write operation.
/// </summary>
public enum WriteOperationType
{
    Insert,
    Update,
    Delete,
    Upsert
}

/// <summary>
/// Default implementation of IWriteContext.
/// </summary>
public sealed class WriteContext : IWriteContext
{
    public Guid ProjectId { get; init; }
    public required TableMetadata Table { get; init; }
    public WriteOperationType OperationType { get; init; }
    public Guid? RecordId { get; init; }
    public IDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();
    public IDictionary<string, object?> OriginalData { get; init; } = new Dictionary<string, object?>();
    public IDictionary<string, object?>? ExistingData { get; init; }
    public IReadOnlyList<string>? KeyColumns { get; init; }
    public WriteOptions Options { get; init; } = WriteOptions.Default;
    public SecurityContext? SecurityContext { get; init; }
    public IList<ValidationError> Errors { get; } = new List<ValidationError>();
    public bool ShouldContinue { get; set; } = true;
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Adds a validation error and optionally stops the pipeline.
    /// </summary>
    public void AddError(string field, string code, string message, object? attemptedValue = null, bool stopPipeline = false)
    {
        Errors.Add(new ValidationError
        {
            Field = field,
            Code = code,
            Message = message,
            AttemptedValue = attemptedValue
        });

        if (stopPipeline)
        {
            ShouldContinue = false;
        }
    }
}
