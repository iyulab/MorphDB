namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for CRUD operations on dynamic tables.
/// </summary>
public interface IMorphDataService
{
    /// <summary>
    /// Gets a query builder for the specified tenant.
    /// </summary>
    IMorphQueryBuilder Query(Guid tenantId);

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new record.
    /// </summary>
    Task<IDictionary<string, object?>> InsertAsync(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    Task<IDictionary<string, object?>> UpdateAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a record.
    /// </summary>
    Task<bool> DeleteAsync(
        Guid tenantId,
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts multiple records in a batch.
    /// </summary>
    Task<IReadOnlyList<IDictionary<string, object?>>> InsertBatchAsync(
        Guid tenantId,
        string tableName,
        IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple records in a batch.
    /// </summary>
    Task<int> UpdateBatchAsync(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple records matching a condition.
    /// </summary>
    Task<int> DeleteBatchAsync(
        Guid tenantId,
        string tableName,
        IMorphQuery whereClause,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts a record (insert or update based on key).
    /// </summary>
    Task<IDictionary<string, object?>> UpsertAsync(
        Guid tenantId,
        string tableName,
        IDictionary<string, object?> data,
        string[] keyColumns,
        CancellationToken cancellationToken = default);
}
