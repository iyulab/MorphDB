namespace MorphDB.Npgsql.Services;

/// <summary>
/// Logs schema changes to the audit trail.
/// </summary>
public interface IChangeLogger
{
    /// <summary>
    /// Logs a schema change operation.
    /// </summary>
    Task LogChangeAsync(
        SchemaChangeEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the change history for a table.
    /// </summary>
    Task<IReadOnlyList<SchemaChangeEntry>> GetHistoryAsync(
        Guid tableId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a schema change entry.
/// </summary>
public sealed record SchemaChangeEntry
{
    public Guid ChangeId { get; init; } = Guid.NewGuid();
    public Guid TableId { get; init; }
    public required SchemaOperation Operation { get; init; }
    public int SchemaVersion { get; init; }
    public required object Changes { get; init; }
    public string? PerformedBy { get; init; }
    public DateTimeOffset PerformedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Types of schema operations.
/// </summary>
public enum SchemaOperation
{
    CreateTable,
    UpdateTable,
    DeleteTable,
    AddColumn,
    UpdateColumn,
    DeleteColumn,
    CreateIndex,
    DeleteIndex,
    CreateRelation,
    DeleteRelation,
    CreateView,
    UpdateView,
    DeleteView,
    RefreshView
}

/// <summary>
/// Extension methods for IChangeLogger to support view operations.
/// </summary>
public static class ChangeLoggerViewExtensions
{
    public static Task LogViewCreatedAsync(
        this IChangeLogger logger,
        Guid viewId,
        string viewName,
        CancellationToken cancellationToken = default)
    {
        return logger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = viewId, // Using TableId field for ViewId
            Operation = SchemaOperation.CreateView,
            Changes = new { viewName }
        }, cancellationToken);
    }

    public static Task LogViewUpdatedAsync(
        this IChangeLogger logger,
        Guid viewId,
        string? viewName,
        CancellationToken cancellationToken = default)
    {
        return logger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = viewId,
            Operation = SchemaOperation.UpdateView,
            Changes = new { viewName }
        }, cancellationToken);
    }

    public static Task LogViewDeletedAsync(
        this IChangeLogger logger,
        Guid viewId,
        string viewName,
        CancellationToken cancellationToken = default)
    {
        return logger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = viewId,
            Operation = SchemaOperation.DeleteView,
            Changes = new { viewName }
        }, cancellationToken);
    }

    public static Task LogViewRefreshedAsync(
        this IChangeLogger logger,
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        return logger.LogChangeAsync(new SchemaChangeEntry
        {
            TableId = viewId,
            Operation = SchemaOperation.RefreshView,
            Changes = new { refreshedAt = DateTimeOffset.UtcNow }
        }, cancellationToken);
    }
}
