namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for querying hierarchical (tree-structured) data using self-referential relations.
/// Uses recursive CTEs internally for efficient tree traversal.
/// </summary>
public interface IHierarchyQueryService
{
    /// <summary>
    /// Gets all ancestors of a record (parent, grandparent, etc.) up to the root.
    /// </summary>
    Task<HierarchyQueryResult> GetAncestorsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all descendants of a record (children, grandchildren, etc.).
    /// </summary>
    Task<HierarchyQueryResult> GetDescendantsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full path from root to the specified record.
    /// </summary>
    Task<HierarchyQueryResult> GetPathToRootAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets siblings (records with same parent) of the specified record.
    /// </summary>
    Task<HierarchyQueryResult> GetSiblingsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the subtree rooted at the specified record (the record itself + all descendants).
    /// </summary>
    Task<HierarchyQueryResult> GetSubtreeAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cycle would be created by setting a new parent.
    /// Returns true if the operation would create a cycle.
    /// </summary>
    Task<bool> WouldCreateCycleAsync(
        CycleCheckRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects all cycles in the hierarchy and returns the affected records.
    /// </summary>
    Task<CycleDetectionResult> DetectCyclesAsync(
        Guid tenantId,
        string tableName,
        string parentColumn,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for hierarchy queries.
/// </summary>
public sealed record HierarchyQueryRequest
{
    /// <summary>
    /// Tenant ID for isolation.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// The table containing hierarchical data.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The column that references the parent record (e.g., "_parent_id" or "parent_id").
    /// </summary>
    public required string ParentColumn { get; init; }

    /// <summary>
    /// The record ID from which to start the traversal.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Maximum depth to traverse. Default uses relation's MaxHierarchyDepth or 100.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    /// Columns to include in the result. Null means all columns.
    /// </summary>
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>
    /// Whether to include the starting record in the result.
    /// </summary>
    public bool IncludeSelf { get; init; }

    /// <summary>
    /// Order by clause for sorting results within each level.
    /// </summary>
    public string? OrderBy { get; init; }
}

/// <summary>
/// Result of a hierarchy query.
/// </summary>
public sealed record HierarchyQueryResult
{
    /// <summary>
    /// The records returned by the query.
    /// </summary>
    public required IReadOnlyList<HierarchyRecord> Records { get; init; }

    /// <summary>
    /// Total count of records in the result.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Maximum depth encountered in the result.
    /// </summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Whether the query reached the maximum depth limit.
    /// </summary>
    public bool ReachedMaxDepth { get; init; }
}

/// <summary>
/// A record in a hierarchy query result with depth information.
/// </summary>
public sealed record HierarchyRecord
{
    /// <summary>
    /// The record data.
    /// </summary>
    public required IDictionary<string, object?> Data { get; init; }

    /// <summary>
    /// Depth level relative to the starting record.
    /// 0 for the starting record, 1 for direct parent/child, etc.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// The path from root to this record as a list of IDs.
    /// </summary>
    public IReadOnlyList<Guid>? Path { get; init; }
}

/// <summary>
/// Request to check if setting a parent would create a cycle.
/// </summary>
public sealed record CycleCheckRequest
{
    public Guid TenantId { get; init; }
    public required string TableName { get; init; }
    public required string ParentColumn { get; init; }

    /// <summary>
    /// The record that would have its parent changed.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// The new parent ID to check.
    /// </summary>
    public Guid NewParentId { get; init; }
}

/// <summary>
/// Result of cycle detection.
/// </summary>
public sealed record CycleDetectionResult
{
    /// <summary>
    /// Whether any cycles were detected.
    /// </summary>
    public bool HasCycles { get; init; }

    /// <summary>
    /// IDs of records that are part of cycles.
    /// </summary>
    public IReadOnlyList<Guid> CyclicRecordIds { get; init; } = [];

    /// <summary>
    /// Description of detected cycles for debugging.
    /// </summary>
    public IReadOnlyList<string> CycleDescriptions { get; init; } = [];
}
