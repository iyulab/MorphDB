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

    // Optional: Row-State (for draft/validation workflow)

    /// <summary>Row state: draft, valid, or error.</summary>
    public const string RowState = "_row_state";

    /// <summary>JSONB array of validation errors when state is 'error'.</summary>
    public const string RowErrors = "_row_errors";

    // Internal columns (not exposed to users)

    /// <summary>Project ID for multi-tenancy isolation (internal, not in API).</summary>
    public const string ProjectId = "project_id";

    /// <summary>
    /// Checks if a column name is a system column.
    /// </summary>
    public static bool IsSystemColumn(string columnName) =>
        columnName.StartsWith('_') || columnName == ProjectId;

    /// <summary>
    /// Extracts the record id from a logical row dictionary via the <see cref="Id"/> column.
    /// Every write-result and query-row dictionary in this codebase is keyed by <see cref="Id"/> —
    /// this is the single, canonical read of it. Returns <c>null</c> when the row has no
    /// <see cref="Id"/> entry or its value cannot be read as a GUID; callers must not fall back to
    /// any other key, since tolerating one masks the row producer's bug instead of surfacing it.
    /// </summary>
    public static Guid? GetRecordId(IDictionary<string, object?>? data)
    {
        if (data is not null && data.TryGetValue(Id, out var value))
        {
            return value switch
            {
                Guid guid => guid,
                string text when Guid.TryParse(text, out var parsed) => parsed,
                _ => null
            };
        }

        return null;
    }

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

    /// <summary>
    /// Returns row-state column names.
    /// </summary>
    public static IReadOnlyList<string> RowStateColumns =>
        [RowState, RowErrors];
}

/// <summary>
/// Possible values for the _row_state system column.
/// </summary>
public enum RowStateValue
{
    /// <summary>Saved but not validated. Used for spreadsheet-style paste operations.</summary>
    Draft,

    /// <summary>Validated successfully. All constraints passed.</summary>
    Valid,

    /// <summary>Validation failed. Errors stored in _row_errors column.</summary>
    Error
}

/// <summary>
/// Represents a single validation error stored in _row_errors column.
/// </summary>
public sealed record RowValidationError
{
    /// <summary>The column name that failed validation.</summary>
    public required string Column { get; init; }

    /// <summary>The error code (e.g., "required", "unique_violation", "check_failed").</summary>
    public required string Error { get; init; }

    /// <summary>Human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional: the value that caused the error.</summary>
    public object? AttemptedValue { get; init; }
}
