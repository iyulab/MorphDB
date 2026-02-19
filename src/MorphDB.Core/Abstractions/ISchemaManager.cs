using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages schema operations (DDL) for dynamic tables.
/// </summary>
public interface ISchemaManager
{
    /// <summary>
    /// Creates a new table with the specified metadata.
    /// </summary>
    Task<TableMetadata> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets table metadata by logical name.
    /// </summary>
    Task<TableMetadata?> GetTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets table metadata by ID.
    /// </summary>
    Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all tables for a tenant.
    /// </summary>
    Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates table metadata (logical name, descriptor).
    /// </summary>
    Task<TableMetadata> UpdateTableAsync(
        UpdateTableRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a table.
    /// </summary>
    Task DeleteTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a column to an existing table.
    /// </summary>
    Task<ColumnMetadata> AddColumnAsync(
        AddColumnRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates column metadata.
    /// </summary>
    Task<ColumnMetadata> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a column (both logical and physical names).
    /// Uses ALTER TABLE ... RENAME COLUMN instead of drop+add to preserve data.
    /// </summary>
    Task<ColumnMetadata> RenameColumnAsync(
        RenameColumnRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a column.
    /// </summary>
    Task DeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an index on a table.
    /// </summary>
    Task<IndexMetadata> CreateIndexAsync(
        CreateIndexRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops an index.
    /// </summary>
    Task DeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a relation between tables.
    /// </summary>
    Task<RelationMetadata> CreateRelationAsync(
        CreateRelationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a relation.
    /// </summary>
    Task DeleteRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken = default);
}

#region Request Models

public sealed record CreateTableRequest
{
    public Guid TenantId { get; init; }
    public required string LogicalName { get; init; }
    public IReadOnlyList<CreateColumnRequest> Columns { get; init; } = [];

    /// <summary>
    /// Options for system columns. If null, defaults are applied.
    /// </summary>
    public SystemColumnOptions? SystemColumns { get; init; }
}

/// <summary>
/// Configuration options for system columns when creating a table.
/// Core columns (_id, _created_at, _updated_at) are always included.
/// </summary>
public sealed record SystemColumnOptions
{
    // Standard columns (default: enabled)

    /// <summary>
    /// Enable _version column for optimistic locking. Default: true.
    /// </summary>
    public bool VersioningEnabled { get; init; } = true;

    /// <summary>
    /// Enable _created_by and _updated_by columns. Default: false.
    /// </summary>
    public bool AuditFieldsEnabled { get; init; }

    // Optional columns (default: disabled)

    /// <summary>
    /// Enable _deleted_at and _deleted_by for soft delete. Default: false.
    /// </summary>
    public bool SoftDeleteEnabled { get; init; }

    /// <summary>
    /// Enable _owner_id for row-level ownership. Default: false.
    /// </summary>
    public bool OwnershipEnabled { get; init; }

    /// <summary>
    /// Enable _parent_id and _sort_order for hierarchical data. Default: false.
    /// </summary>
    public bool HierarchyEnabled { get; init; }

    /// <summary>
    /// Enable _source_id for external system tracking. Default: false.
    /// </summary>
    public bool SourceTrackingEnabled { get; init; }

    /// <summary>
    /// Enable _row_state and _row_errors for draft mode and deferred validation. Default: false.
    /// </summary>
    public bool RowStateEnabled { get; init; }
}

public sealed record UpdateTableRequest
{
    public Guid TableId { get; init; }
    public string? LogicalName { get; init; }
    public int ExpectedVersion { get; init; }
}

public sealed record CreateColumnRequest
{
    public required string LogicalName { get; init; }
    public required MorphDataType DataType { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsIndexed { get; init; }
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Configuration for lookup fields that reference related table data.
    /// When set, the column becomes a virtual lookup column.
    /// </summary>
    public LookupColumnConfig? LookupConfig { get; init; }

    /// <summary>
    /// Configuration for rollup fields that aggregate data from related tables.
    /// When set, the column becomes a virtual rollup column.
    /// </summary>
    public RollupColumnConfig? RollupConfig { get; init; }

    /// <summary>
    /// Configuration for formula fields that compute values from expressions.
    /// When set, the column becomes a virtual formula column.
    /// </summary>
    public FormulaColumnConfig? FormulaConfig { get; init; }
}

public sealed record AddColumnRequest
{
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required MorphDataType DataType { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsUnique { get; init; }
    public bool IsIndexed { get; init; }
    public string? DefaultValue { get; init; }
    public int ExpectedVersion { get; init; }

    /// <summary>
    /// Configuration for lookup fields that reference related table data.
    /// When set, the column becomes a virtual lookup column.
    /// </summary>
    public LookupColumnConfig? LookupConfig { get; init; }

    /// <summary>
    /// Configuration for rollup fields that aggregate data from related tables.
    /// When set, the column becomes a virtual rollup column.
    /// </summary>
    public RollupColumnConfig? RollupConfig { get; init; }

    /// <summary>
    /// Configuration for formula fields that compute values from expressions.
    /// When set, the column becomes a virtual formula column.
    /// </summary>
    public FormulaColumnConfig? FormulaConfig { get; init; }
}

public sealed record UpdateColumnRequest
{
    public Guid ColumnId { get; init; }
    public string? LogicalName { get; init; }
    public string? DefaultValue { get; init; }
    public int ExpectedVersion { get; init; }
}

public sealed record RenameColumnRequest
{
    public Guid ColumnId { get; init; }
    public required string NewLogicalName { get; init; }
    public int ExpectedVersion { get; init; }
}

public sealed record CreateIndexRequest
{
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required IReadOnlyList<Guid> ColumnIds { get; init; }
    public IndexType IndexType { get; init; } = IndexType.BTree;
    public bool IsUnique { get; init; }
    public string? WhereClause { get; init; }
}

public sealed record CreateRelationRequest
{
    public Guid TenantId { get; init; }
    public required string LogicalName { get; init; }
    public Guid SourceTableId { get; init; }
    public Guid SourceColumnId { get; init; }
    public Guid TargetTableId { get; init; }
    public Guid TargetColumnId { get; init; }
    public RelationType RelationType { get; init; }
    public OnDeleteAction OnDelete { get; init; } = OnDeleteAction.NoAction;

    /// <summary>
    /// For ManyToMany relations, specify a custom junction table name.
    /// If null, auto-generates as "{SourceTable}_{TargetTable}".
    /// </summary>
    public string? JunctionTableName { get; init; }

    /// <summary>
    /// Maximum depth for hierarchy traversal (self-referential relations only).
    /// Default is 100.
    /// </summary>
    public int MaxHierarchyDepth { get; init; } = 100;
}

#endregion
