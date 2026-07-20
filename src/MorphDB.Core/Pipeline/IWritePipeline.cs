using MorphDB.Core.Models;

namespace MorphDB.Core.Pipeline;

/// <summary>
/// The write pipeline that orchestrates validation and transformation.
/// </summary>
public interface IWritePipeline
{
    /// <summary>
    /// Executes the pipeline for an insert operation.
    /// </summary>
    Task<WriteResult> InsertAsync(
        Guid projectId,
        TableMetadata table,
        IDictionary<string, object?> data,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the pipeline for an update operation.
    /// </summary>
    Task<WriteResult> UpdateAsync(
        Guid projectId,
        TableMetadata table,
        Guid recordId,
        IDictionary<string, object?> data,
        IDictionary<string, object?>? existingData = null,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the pipeline for a delete operation.
    /// </summary>
    Task<WriteResult> DeleteAsync(
        Guid projectId,
        TableMetadata table,
        Guid recordId,
        IDictionary<string, object?>? existingData = null,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates data without writing (dry run).
    /// </summary>
    Task<WriteResult> ValidateAsync(
        Guid projectId,
        TableMetadata table,
        IDictionary<string, object?> data,
        WriteOperationType operationType,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a batch of records (for deferred validation after bulk import).
    /// </summary>
    Task<IReadOnlyList<WriteResult>> ValidateBatchAsync(
        Guid projectId,
        TableMetadata table,
        IReadOnlyList<IDictionary<string, object?>> records,
        WriteOptions? options = null,
        CancellationToken cancellationToken = default);
}
