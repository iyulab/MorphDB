using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents a relationship between tables.
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
