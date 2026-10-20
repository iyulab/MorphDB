using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents metadata for a dynamic table.
/// </summary>
/// <remarks>
/// A record, so a copy that changes one part is written <c>table with { Columns = … }</c>: a copy that
/// names every property by hand drops whichever property is added next, silently.
/// </remarks>
public sealed record TableMetadata
{
    public Guid TableId { get; init; }
    public Guid ProjectId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public int SchemaVersion { get; init; }
    public JsonDocument? Descriptor { get; init; }

    /// <summary>
    /// The creator's notes recorded with the table when it was created; empty when none were given.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsActive { get; init; } = true;

    public IReadOnlyList<ColumnMetadata> Columns { get; init; } = [];
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];
    public IReadOnlyList<IndexMetadata> Indexes { get; init; } = [];

    // Virtual Constraint Properties

    /// <summary>
    /// When true, records are soft-deleted using _deleted_at column.
    /// DELETE operations set _deleted_at instead of removing rows.
    /// </summary>
    public bool SoftDeleteEnabled { get; init; }

    /// <summary>
    /// When true, optimistic locking is enabled using _version column.
    /// Updates require version match and auto-increment the version.
    /// </summary>
    public bool VersioningEnabled { get; init; }

    /// <summary>
    /// When true, _created_at and _updated_at are auto-managed.
    /// </summary>
    public bool TimestampsEnabled { get; init; } = true;

    /// <summary>
    /// When true, _created_by and _updated_by are auto-managed.
    /// </summary>
    public bool AuditFieldsEnabled { get; init; }

    // Optional System Column Properties

    /// <summary>
    /// When true, _owner_id column is added for row-level ownership.
    /// Enables ownership-based access control and filtering.
    /// </summary>
    public bool OwnershipEnabled { get; init; }

    /// <summary>
    /// When true, _parent_id and _sort_order columns are added for hierarchical data.
    /// Supports tree structures like folders, categories, nested comments.
    /// </summary>
    public bool HierarchyEnabled { get; init; }

    /// <summary>
    /// When true, _source_id column is added for external system tracking.
    /// Used for sync operations and external system integration.
    /// </summary>
    public bool SourceTrackingEnabled { get; init; }

    /// <summary>
    /// When true, _row_state and _row_errors columns are added for row-level validation workflow.
    /// Enables draft mode inserts, deferred validation, and spreadsheet-style paste operations.
    /// </summary>
    public bool RowStateEnabled { get; init; }
}
