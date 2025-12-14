using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Repository for managing schema metadata in system tables.
/// </summary>
public interface IMetadataRepository
{
    #region Table Operations

    Task<TableMetadata> InsertTableAsync(
        TableMetadata table,
        CancellationToken cancellationToken = default);

    Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task<TableMetadata?> GetTableByNameAsync(
        Guid tenantId,
        string logicalName,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid tenantId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task UpdateTableAsync(
        Guid tableId,
        string? logicalName,
        int newVersion,
        CancellationToken cancellationToken = default);

    Task SoftDeleteTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Column Operations

    Task<ColumnMetadata> InsertColumnAsync(
        ColumnMetadata column,
        CancellationToken cancellationToken = default);

    Task<ColumnMetadata?> GetColumnByIdAsync(
        Guid columnId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ColumnMetadata>> GetColumnsByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task<int> GetNextOrdinalPositionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task UpdateColumnAsync(
        Guid columnId,
        string? logicalName,
        string? defaultValue,
        CancellationToken cancellationToken = default);

    Task SoftDeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Index Operations

    Task<IndexMetadata> InsertIndexAsync(
        IndexMetadata index,
        CancellationToken cancellationToken = default);

    Task<IndexMetadata?> GetIndexByIdAsync(
        Guid indexId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexMetadata>> GetIndexesByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task SoftDeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Relation Operations

    Task<RelationMetadata> InsertRelationAsync(
        RelationMetadata relation,
        CancellationToken cancellationToken = default);

    Task<RelationMetadata?> GetRelationByIdAsync(
        Guid relationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RelationMetadata>> GetRelationsByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task SoftDeleteRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Version Operations

    Task<int> GetCurrentVersionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task IncrementVersionAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    #endregion
}
