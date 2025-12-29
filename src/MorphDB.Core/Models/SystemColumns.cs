namespace MorphDB.Core.Models;

/// <summary>
/// Centralized definition of system column names and metadata.
/// All system columns use underscore prefix and are not hashed.
/// </summary>
public static class SystemColumns
{
    // Core columns (always present, cannot be disabled)

    /// <summary>Primary key column using UUID v7 for time-sortable IDs.</summary>
    public const string Id = "_id";

    /// <summary>Record creation timestamp, immutable after insert.</summary>
    public const string CreatedAt = "_created_at";

    /// <summary>Record modification timestamp, updated on every change.</summary>
    public const string UpdatedAt = "_updated_at";

    // Standard columns (enabled by default, can be disabled)

    /// <summary>Optimistic locking version, incremented on each update.</summary>
    public const string Version = "_version";

    /// <summary>User ID who created the record.</summary>
    public const string CreatedBy = "_created_by";

    /// <summary>User ID who last modified the record.</summary>
    public const string UpdatedBy = "_updated_by";

    // Optional: Soft Delete

    /// <summary>Soft delete timestamp, NULL means active.</summary>
    public const string DeletedAt = "_deleted_at";

    /// <summary>User ID who soft-deleted the record.</summary>
    public const string DeletedBy = "_deleted_by";

    // Optional: Ownership

    /// <summary>Owner user ID for row-level access control.</summary>
    public const string OwnerId = "_owner_id";

    // Optional: Hierarchy

    /// <summary>Parent record reference for tree structures.</summary>
    public const string ParentId = "_parent_id";

    /// <summary>Sort order within same parent for drag-and-drop.</summary>
    public const string SortOrder = "_sort_order";

    // Optional: Source Tracking

    /// <summary>External system's original ID for sync operations.</summary>
    public const string SourceId = "_source_id";

    // Internal columns (not exposed to users)

    /// <summary>Tenant ID for multi-tenancy isolation (internal, not in API).</summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// Checks if a column name is a system column.
    /// </summary>
    public static bool IsSystemColumn(string columnName) =>
        columnName.StartsWith('_') || columnName == TenantId;

    /// <summary>
    /// Returns all core column names that are always present.
    /// </summary>
    public static IReadOnlyList<string> CoreColumns =>
        [Id, CreatedAt, UpdatedAt];

    /// <summary>
    /// Returns standard column names (version + audit).
    /// </summary>
    public static IReadOnlyList<string> StandardColumns =>
        [Version, CreatedBy, UpdatedBy];

    /// <summary>
    /// Returns soft delete column names.
    /// </summary>
    public static IReadOnlyList<string> SoftDeleteColumns =>
        [DeletedAt, DeletedBy];

    /// <summary>
    /// Returns ownership column names.
    /// </summary>
    public static IReadOnlyList<string> OwnershipColumns =>
        [OwnerId];

    /// <summary>
    /// Returns hierarchy column names.
    /// </summary>
    public static IReadOnlyList<string> HierarchyColumns =>
        [ParentId, SortOrder];

    /// <summary>
    /// Returns source tracking column names.
    /// </summary>
    public static IReadOnlyList<string> SourceTrackingColumns =>
        [SourceId];
}
