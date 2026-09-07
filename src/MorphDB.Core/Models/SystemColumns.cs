using System.Diagnostics;

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
    /// Whether a column is internal: carried by every table, part of no consumer-facing surface.
    /// Today that is <see cref="ProjectId"/> alone. Every surface that lists, projects, maps or
    /// exports a table's columns filters through this one predicate — REST and GraphQL schema
    /// responses, row mapping, the available-columns error text, bulk export — so the set of
    /// surfaces that know the rule cannot drift apart again.
    /// </summary>
    public static bool IsInternal(string columnName) => columnName == ProjectId;

    /// <summary>
    /// The columns a consumer may see or name: <see cref="TableMetadata.Columns"/> without the
    /// internal ones.
    /// </summary>
    public static IEnumerable<ColumnMetadata> ExposedColumns(this TableMetadata table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return table.Columns.Where(c => !IsInternal(c.LogicalName));
    }

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
    /// The record id a row must carry. Every row a write returns and every row a read produces
    /// with <see cref="Id"/> in its projection has one; a row without it is a producer's bug, and
    /// answering it with <see cref="Guid.Empty"/> — as several surfaces once did — hands the caller
    /// a well-formed id that is the same for every such row, which is worse than failing. This
    /// fails instead.
    /// </summary>
    /// <exception cref="UnreachableException">The row has no readable <see cref="Id"/>. An invariant
    /// violation rather than an <see cref="InvalidOperationException"/>, so that no request-level
    /// handler that answers the latter with a 4xx can turn a server bug into a caller error.</exception>
    public static Guid RequireRecordId(IDictionary<string, object?>? data) =>
        GetRecordId(data) ?? throw new UnreachableException(
            $"The row carries no '{Id}' column; every row read or written through MorphDB must, so this is a producer bug, not a caller error.");

    /// <summary>
    /// The caller's column selection with <see cref="Id"/> guaranteed present, in first position when
    /// it had to be added. The REST envelope names each row by its id, so a projection that drops the
    /// id column cannot produce a well-formed response; rather than answer with a placeholder id, the
    /// id column is always fetched.
    /// </summary>
    public static IReadOnlyList<string> WithRecordId(IEnumerable<string> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        var list = columns.ToList();
        if (!list.Contains(Id, StringComparer.Ordinal))
        {
            list.Insert(0, Id);
        }
        return list;
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
