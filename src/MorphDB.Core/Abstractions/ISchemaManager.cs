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
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets table metadata by ID.
    /// </summary>
    Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all tables for a project.
    /// </summary>
    Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid projectId,
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

    /// <summary>
    /// Executes a batch of DDL operations atomically within a single transaction.
    /// If any operation fails, all operations are rolled back.
    /// </summary>
    Task<BatchDdlResult> ExecuteBatchDdlAsync(
        BatchDdlRequest request,
        CancellationToken cancellationToken = default);
}

#region Batch DDL Models

/// <summary>
/// Request to execute multiple DDL operations atomically.
/// </summary>
public sealed record BatchDdlRequest
{
    public Guid TableId { get; init; }
    public int ExpectedVersion { get; init; }
    public required IReadOnlyList<BatchDdlOperation> Operations { get; init; }
}

/// <summary>
/// A single DDL operation within a batch.
/// </summary>
public sealed record BatchDdlOperation
{
    public required string Type { get; init; } // addColumn, updateColumn, deleteColumn, createIndex, deleteIndex, createRelation, deleteRelation
    public AddColumnRequest? AddColumn { get; init; }
    public UpdateColumnRequest? UpdateColumn { get; init; }
    public Guid? DeleteColumnId { get; init; }
    public CreateIndexRequest? CreateIndex { get; init; }
    public Guid? DeleteIndexId { get; init; }
    public CreateRelationRequest? CreateRelation { get; init; }
    public Guid? DeleteRelationId { get; init; }
}

/// <summary>
/// Result of a batch DDL execution.
/// </summary>
public sealed record BatchDdlResult
{
    public bool Success { get; init; }
    public int OperationsExecuted { get; init; }
    public int NewSchemaVersion { get; init; }
    public string? Error { get; init; }
}

#endregion

#region Request Models

public sealed record CreateTableRequest
{
    public Guid ProjectId { get; init; }
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
    /// SQL CHECK constraint expression (e.g. "price > 0 AND price &lt; 10000").
    /// Enforced at both application and database level.
    /// </summary>
    public string? CheckExpression { get; init; }

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
    /// SQL CHECK constraint expression (e.g. "quantity >= 0 AND quantity &lt;= 10000").
    /// Enforced at both application and database level.
    /// </summary>
    public string? CheckExpression { get; init; }

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
    public MorphDataType? DataType { get; init; }
    public bool? IsNullable { get; init; }
    public bool? IsUnique { get; init; }
    public string? CheckExpression { get; init; }
    public int ExpectedVersion { get; init; }

    /// <summary>
    /// When true, forces type changes even if the conversion is not in the safe-cast list.
    /// PostgreSQL will attempt the cast using USING column::new_type.
    /// If the cast fails at the database level, the operation rolls back.
    /// Default: false (only safe, widening conversions are allowed).
    /// </summary>
    public bool ForceCast { get; init; }
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
    public Guid ProjectId { get; init; }
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

    /// <summary>
    /// Whether writes are validated against this relation. Null, the default, defers to the
    /// project's <see cref="ProjectSettings.DefaultEnforceOnWrite"/>, which is itself true unless
    /// the project says otherwise.
    /// <para>
    /// Set false to declare the link without checking it. This is what a caller that rebuilds its
    /// tables wholesale needs: when tables are dropped and reloaded independently, a child can be
    /// written before its parent has been reloaded, and enforcement would reject data that is in
    /// fact consistent at the source. The relation is still recorded, so joins and navigation see
    /// it — it just does not gate writes.
    /// </para>
    /// <para>
    /// Whatever this resolves to is stored on the relation and echoed back, so the answer is
    /// readable afterwards rather than having to be recomputed from settings.
    /// </para>
    /// </summary>
    public bool? EnforceOnWrite { get; init; }

    /// <summary>
    /// Whether cascade behaviour is handled by the application layer (soft-delete aware).
    /// Default true.
    /// </summary>
    public bool VirtualCascade { get; init; } = true;
}

#endregion
