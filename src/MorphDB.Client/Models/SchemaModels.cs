namespace MorphDB.Client.Models;

// These models mirror the server schema wire contract (MorphDB.Service ApiModels:
// CreateTableApiRequest / CreateColumnApiRequest / AddColumnApiRequest / UpdateColumnApiRequest /
// TableApiResponse / ColumnApiResponse). Property names match the server's so the default
// System.Text.Json Web (camelCase) serialization used by HttpClient round-trips correctly.
// Do not reintroduce fields the server does not send (e.g. physicalName, nativeType) — required
// members with no wire source make response deserialization throw.

/// <summary>
/// Request to create a new table.
/// </summary>
public sealed class CreateTableRequest
{
    /// <summary>
    /// Table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column definitions.
    /// </summary>
    public IReadOnlyList<CreateColumnRequest> Columns { get; init; } = [];

    /// <summary>
    /// Optional system column configuration (timestamps, versioning, soft delete, etc.).
    /// </summary>
    public SystemColumnOptions? SystemColumns { get; init; }
}

/// <summary>
/// System column configuration for a table (mirrors the server's SystemColumnOptions request).
/// </summary>
public sealed class SystemColumnOptions
{
    /// <summary>Enable the _version column for optimistic locking. Default: true.</summary>
    public bool Versioning { get; init; } = true;

    /// <summary>Enable _created_by / _updated_by columns.</summary>
    public bool AuditFields { get; init; }

    /// <summary>Enable _deleted_at / _deleted_by for soft delete.</summary>
    public bool SoftDelete { get; init; }

    /// <summary>Enable _owner_id for row-level ownership.</summary>
    public bool Ownership { get; init; }

    /// <summary>Enable _parent_id / _sort_order for hierarchical data.</summary>
    public bool Hierarchy { get; init; }

    /// <summary>Enable _source_id for external system tracking.</summary>
    public bool SourceTracking { get; init; }

    /// <summary>Enable _row_state / _row_errors for draft mode and deferred validation.</summary>
    public bool RowState { get; init; }
}

/// <summary>
/// Request to create a column.
/// </summary>
public sealed class CreateColumnRequest
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column type (text, integer, boolean, timestamp, uuid, jsonb, etc.).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool Nullable { get; init; } = true;

    /// <summary>
    /// Whether the column has a unique constraint.
    /// </summary>
    public bool Unique { get; init; }

    /// <summary>
    /// Whether to create an index on this column.
    /// </summary>
    public bool Indexed { get; init; }

    /// <summary>
    /// Default value expression.
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? Check { get; init; }
}

/// <summary>
/// Request to add a column to an existing table.
/// </summary>
public sealed class AddColumnRequest
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool Nullable { get; init; } = true;

    /// <summary>
    /// Whether the column has a unique constraint.
    /// </summary>
    public bool Unique { get; init; }

    /// <summary>
    /// Whether to create an index on this column.
    /// </summary>
    public bool Indexed { get; init; }

    /// <summary>
    /// Default value expression.
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? Check { get; init; }
}

/// <summary>
/// Request to alter a column (mirrors the server's UpdateColumnApiRequest).
/// </summary>
public sealed class AlterColumnRequest
{
    /// <summary>
    /// New column name (for rename).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// New data type.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// New nullability setting.
    /// </summary>
    public bool? Nullable { get; init; }

    /// <summary>
    /// New unique constraint setting.
    /// </summary>
    public bool? Unique { get; init; }

    /// <summary>
    /// New default value.
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// New check expression.
    /// </summary>
    public string? Check { get; init; }

    /// <summary>
    /// Expected current schema version for optimistic concurrency.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// When true, forces type conversion even if it may cause data loss.
    /// </summary>
    public bool ForceCast { get; init; }
}

/// <summary>
/// Table information response.
/// </summary>
public sealed class TableInfo
{
    /// <summary>
    /// Table ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Logical table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Current schema version.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Column definitions.
    /// </summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Column information response.
/// </summary>
public sealed class ColumnInfo
{
    /// <summary>
    /// Column ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Logical column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Abstract data type (text, integer, boolean, timestamp, uuid, jsonb, etc.).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool Nullable { get; init; }

    /// <summary>
    /// Whether the column has a unique constraint.
    /// </summary>
    public bool Unique { get; init; }

    /// <summary>
    /// Whether the column is the primary key.
    /// </summary>
    public bool PrimaryKey { get; init; }

    /// <summary>
    /// Whether the column is indexed.
    /// </summary>
    public bool Indexed { get; init; }

    /// <summary>
    /// Default value expression.
    /// </summary>
    public string? Default { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? Check { get; init; }

    /// <summary>
    /// Ordinal position in the table.
    /// </summary>
    public int Position { get; init; }

    /// <summary>
    /// Whether this is a derived/virtual column (lookup, rollup, formula).
    /// </summary>
    public bool IsDerived { get; init; }
}

/// <summary>
/// Request to create a relation between two tables (mirrors the server's
/// <c>CreateRelationApiRequest</c>).
/// </summary>
public sealed class CreateRelationRequest
{
    /// <summary>
    /// Relation name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Logical name of the table holding the referencing column.
    /// </summary>
    public required string SourceTable { get; init; }

    /// <summary>
    /// Logical name of the referencing column.
    /// </summary>
    public required string SourceColumn { get; init; }

    /// <summary>
    /// Logical name of the referenced table.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// Logical name of the referenced column, usually <c>_id</c>.
    /// </summary>
    public required string TargetColumn { get; init; }

    /// <summary>
    /// Relation type: <c>one-to-one</c>, <c>one-to-many</c> or <c>many-to-many</c>.
    /// Default: <c>one-to-many</c>.
    /// </summary>
    public string Type { get; init; } = "one-to-many";

    /// <summary>
    /// Delete behaviour: <c>no-action</c>, <c>cascade</c>, <c>set-null</c>, <c>set-default</c>
    /// or <c>restrict</c>. Default: <c>no-action</c>.
    /// </summary>
    public string OnDelete { get; init; } = "no-action";

    /// <summary>
    /// Whether writes are validated against this relation. Default: true.
    /// <para>
    /// Set false to declare the link without gating writes on it: joins and navigation still see
    /// it, but a row referencing a missing parent is accepted, and no physical constraint is
    /// created either. This is what a caller that rebuilds tables wholesale needs — when tables are
    /// dropped and reloaded independently, a child can be written before its parent has been
    /// reloaded, and enforcing would reject data that is consistent at its source.
    /// </para>
    /// </summary>
    public bool EnforceOnWrite { get; init; } = true;

    /// <summary>
    /// Whether cascade behaviour is handled by the application layer. Default: true.
    /// </summary>
    public bool VirtualCascade { get; init; } = true;
}

/// <summary>
/// A relation as the server reports it (mirrors the server's <c>RelationApiResponse</c>).
/// </summary>
public sealed class RelationInfo
{
    /// <summary>
    /// Relation identifier — what <c>DeleteRelationAsync</c> takes.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Relation name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Relation type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Delete behaviour.
    /// </summary>
    public required string OnDelete { get; init; }

    /// <summary>
    /// Whether writes are validated against this relation, as stored.
    /// </summary>
    public bool EnforceOnWrite { get; init; }

    /// <summary>
    /// Whether cascade behaviour is handled by the application layer, as stored.
    /// </summary>
    public bool VirtualCascade { get; init; }
}
