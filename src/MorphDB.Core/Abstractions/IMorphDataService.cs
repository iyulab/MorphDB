namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for CRUD operations on dynamic tables.
/// </summary>
public interface IMorphDataService
{
    /// <summary>
    /// Gets a query builder for the specified project.
    /// </summary>
    IMorphQueryBuilder Query(Guid projectId);

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new record.
    /// </summary>
    Task<IDictionary<string, object?>> InsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    Task<IDictionary<string, object?>> UpdateAsync(
        Guid projectId,
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a record.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid projectId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts multiple records in a batch.
    /// </summary>
    Task<IReadOnlyList<IDictionary<string, object?>>> InsertBatchAsync(
        Guid projectId,
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple records in a batch.
    /// </summary>
    Task<int> UpdateBatchAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple records matching a condition.
    /// </summary>
    Task<int> DeleteBatchAsync(
        Guid projectId,
        string tableName,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a record (insert or update based on key).
    /// </summary>
    Task<IDictionary<string, object?>> UpsertAsync(
        Guid projectId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default);
}
