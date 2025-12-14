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
}

public sealed record UpdateColumnRequest
{
    public Guid ColumnId { get; init; }
    public string? LogicalName { get; init; }
    public string? DefaultValue { get; init; }
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
}

#endregion
