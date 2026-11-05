using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Repository for managing view metadata in system tables.
/// </summary>
public interface IViewMetadataRepository
{
    /// <summary>
    /// Inserts a new view metadata record.
    /// </summary>
    Task<ViewMetadata> InsertViewAsync(
        ViewMetadata view,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets view metadata by ID.
    /// </summary>
    Task<ViewMetadata?> GetViewByIdAsync(
        Guid viewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets view metadata by logical name within a project.
    /// </summary>
    Task<ViewMetadata?> GetViewByNameAsync(
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all views for a project.
    /// </summary>
    Task<IReadOnlyList<ViewMetadata>> ListViewsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates view metadata.
    /// </summary>
    Task UpdateViewAsync(
        Guid viewId,
        string? logicalName,
        ViewDefinition? definition,
        MaterializedViewRefreshPolicy? refreshPolicy,
        string? refreshSchedule,
        string? description,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last refresh timestamp for a materialized view.
    /// </summary>
    Task UpdateLastRefreshAsync(
        Guid viewId,
        DateTimeOffset refreshedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a materialized view as stale or not stale.
    /// </summary>
    Task UpdateStaleStatusAsync(
        Guid viewId,
        bool isStale,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a view.
    /// </summary>
    Task SoftDeleteViewAsync(
        Guid viewId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a view column metadata record.
    /// </summary>
    Task<ViewColumnMetadata> InsertViewColumnAsync(
        ViewColumnMetadata column,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all columns for a view.
    /// </summary>
    Task<IReadOnlyList<ViewColumnMetadata>> GetViewColumnsAsync(
        Guid viewId,
        CancellationToken cancellationToken = default);
}
