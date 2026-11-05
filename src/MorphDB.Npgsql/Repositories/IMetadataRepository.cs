using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Repository for managing schema metadata in system tables.
/// </summary>
public interface IMetadataRepository
{
    #region Table Operations

    Task<TableMetadata> InsertTableAsync(
        TableMetadata table,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task<TableMetadata?> GetTableByNameAsync(
        Guid projectId,
        string logicalName,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid projectId,
        bool includeColumns = false,
        CancellationToken cancellationToken = default);

    Task UpdateTableAsync(
        Guid tableId,
        string? logicalName,
        int newVersion,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task SoftDeleteTableAsync(
        Guid tableId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Column Operations

    Task<ColumnMetadata> InsertColumnAsync(
        ColumnMetadata column,
        NpgsqlTransaction? transaction = null,
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
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task UpdateColumnMetadataAsync(
        Guid columnId,
        string? dataType,
        string? nativeType,
        bool? isNullable,
        bool? isUnique,
        string? checkExpression,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task SoftDeleteColumnAsync(
        Guid columnId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Index Operations

    Task<IndexMetadata> InsertIndexAsync(
        IndexMetadata index,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IndexMetadata?> GetIndexByIdAsync(
        Guid indexId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndexMetadata>> GetIndexesByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task SoftDeleteIndexAsync(
        Guid indexId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Relation Operations

    Task<RelationMetadata> InsertRelationAsync(
        RelationMetadata relation,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<RelationMetadata?> GetRelationByIdAsync(
        Guid relationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RelationMetadata>> GetRelationsByTableIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default);

    Task SoftDeleteRelationAsync(
        Guid relationId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Version Operations

    Task<int> GetCurrentVersionAsync(
        Guid tableId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task IncrementVersionAsync(
        Guid tableId,
        NpgsqlTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    #endregion

    /// <summary>
    /// Empties whatever this repository keeps in memory. A write given a <c>transaction</c> leaves it alone
    /// — nobody else can see the write yet — so whoever commits that transaction calls this afterwards.
    /// </summary>
    void InvalidateCache();
}
