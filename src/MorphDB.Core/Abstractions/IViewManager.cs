using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages view definitions and operations.
/// Views are virtual tables derived from queries over base tables.
/// </summary>
public interface IViewManager
{
    /// <summary>
    /// Creates a new view with the specified definition.
    /// </summary>
    Task<ViewMetadata> CreateViewAsync(
        CreateViewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets view metadata by logical name.
    /// </summary>
    Task<ViewMetadata?> GetViewAsync(
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets view metadata by ID.
    /// </summary>
    Task<ViewMetadata?> GetViewByIdAsync(
        Guid viewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all views for a project.
    /// </summary>
    Task<IReadOnlyList<ViewMetadata>> ListViewsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a view's definition or metadata.
    /// </summary>
    Task<ViewMetadata> UpdateViewAsync(
        UpdateViewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a view (soft delete).
    /// </summary>
    Task DeleteViewAsync(
        Guid viewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes a materialized view's data.
    /// </summary>
    Task RefreshMaterializedViewAsync(
        Guid viewId,
        bool concurrent = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a materialized view is stale (base tables have changed).
    /// </summary>
    Task<bool> IsMaterializedViewStaleAsync(
        Guid viewId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries data from a view.
    /// </summary>
    Task<ViewQueryResult> QueryViewAsync(
        ViewQueryRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new view.
/// </summary>
public sealed class CreateViewRequest
{
    public Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public required ViewDefinition Definition { get; init; }
    public bool IsMaterialized { get; init; }
    public MaterializedViewRefreshPolicy RefreshPolicy { get; init; } = MaterializedViewRefreshPolicy.OnDemand;
    public string? RefreshSchedule { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request to update an existing view.
/// </summary>
public sealed class UpdateViewRequest
{
    public Guid ViewId { get; init; }
    public string? Name { get; init; }
    public ViewDefinition? Definition { get; init; }
    public MaterializedViewRefreshPolicy? RefreshPolicy { get; init; }
    public string? RefreshSchedule { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request to query data from a view.
/// </summary>
public sealed class ViewQueryRequest
{
    public Guid ProjectId { get; init; }
    public required string ViewName { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public IReadOnlyList<ViewFilterSpec>? Filters { get; init; }
    public IReadOnlyList<ViewOrderSpec>? OrderBy { get; init; }
    public int? Skip { get; init; }
    public int? Take { get; init; }
}

/// <summary>
/// Result of a view query.
/// </summary>
public sealed class ViewQueryResult
{
    public required IReadOnlyList<IDictionary<string, object?>> Data { get; init; }
    public long TotalCount { get; init; }
    public bool HasMore { get; init; }
    public ViewMetadata? Metadata { get; init; }
}
