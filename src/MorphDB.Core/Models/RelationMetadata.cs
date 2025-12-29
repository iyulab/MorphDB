using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents a relationship between tables (Virtual FK).
/// Foreign keys are NOT created in the database; they are enforced at application layer.
/// </summary>
public sealed class RelationMetadata
{
    public Guid RelationId { get; init; }
    public Guid TenantId { get; init; }

    /// <summary>
    /// The table containing the foreign key.
    /// </summary>
    public Guid SourceTableId { get; init; }

    /// <summary>
    /// The column in the source table that references the target.
    /// </summary>
    public Guid SourceColumnId { get; init; }

    /// <summary>
    /// The table being referenced.
    /// </summary>
    public Guid TargetTableId { get; init; }

    /// <summary>
    /// The column being referenced (usually primary key).
    /// </summary>
    public Guid TargetColumnId { get; init; }

    public required string LogicalName { get; init; }
    public RelationType RelationType { get; init; }
    public OnDeleteAction OnDelete { get; init; } = OnDeleteAction.NoAction;
    public OnUpdateAction OnUpdate { get; init; } = OnUpdateAction.NoAction;
    public JsonDocument? Descriptor { get; init; }
    public bool IsActive { get; init; } = true;

    // Virtual Constraint Properties

    /// <summary>
    /// Logical name of the source table (for validation).
    /// </summary>
    public string? SourceTableName { get; init; }

    /// <summary>
    /// Logical name of the source column (for validation).
    /// </summary>
    public string? SourceColumnName { get; init; }

    /// <summary>
    /// Logical name of the target table (for validation).
    /// </summary>
    public string? TargetTableName { get; init; }

    /// <summary>
    /// Logical name of the target column (for validation).
    /// </summary>
    public string? TargetColumnName { get; init; }

    /// <summary>
    /// When true, FK constraint is validated on write operations.
    /// Set to false to allow orphan records (useful for bulk imports).
    /// </summary>
    public bool EnforceOnWrite { get; init; } = true;

    /// <summary>
    /// When true, cascade operations are handled by the application layer.
    /// This enables soft-delete aware cascades.
    /// </summary>
    public bool VirtualCascade { get; init; } = true;
}

public enum RelationType
{
    OneToOne,
    OneToMany,
    ManyToMany
}

public enum OnDeleteAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
    Restrict
}

public enum OnUpdateAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault,
    Restrict
}
