using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Caching;

/// <summary>
/// Decorator that adds caching to ISchemaManager operations.
/// </summary>
public sealed class CachingSchemaManagerDecorator : ISchemaManager
{
    private readonly ISchemaManager _inner;
    private readonly ISchemaCache _cache;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILogger<CachingSchemaManagerDecorator> _logger;

    public CachingSchemaManagerDecorator(
        ISchemaManager inner,
        ISchemaCache cache,
        IMetadataRepository metadataRepository,
        ILogger<CachingSchemaManagerDecorator> logger)
    {
        _inner = inner;
        _cache = cache;
        _metadataRepository = metadataRepository;
        _logger = logger;
    }

    public async Task<TableMetadata> CreateTableAsync(
        CreateTableRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await _inner.CreateTableAsync(request, cancellationToken);

        // Cache the new table and invalidate tenant list
        await _cache.SetTableAsync(table, cancellationToken);
        await _cache.InvalidateTenantTablesAsync(request.TenantId, cancellationToken);

        return table;
    }

    public async Task<TableMetadata?> GetTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cached = await _cache.GetTableAsync(tenantId, logicalName, cancellationToken);
        if (cached is not null)
        {
            CachingSchemaManagerLogs.CacheHit(_logger, logicalName);
            return cached;
        }

        CachingSchemaManagerLogs.CacheMiss(_logger, logicalName);

        // Fetch from database
        var table = await _inner.GetTableAsync(tenantId, logicalName, cancellationToken);
        if (table is not null)
        {
            await _cache.SetTableAsync(table, cancellationToken);
        }

        return table;
    }

    public async Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cached = await _cache.GetTableByIdAsync(tableId, cancellationToken);
        if (cached is not null)
        {
            CachingSchemaManagerLogs.CacheHitById(_logger, tableId);
            return cached;
        }

        CachingSchemaManagerLogs.CacheMissById(_logger, tableId);

        // Fetch from database
        var table = await _inner.GetTableByIdAsync(tableId, cancellationToken);
        if (table is not null)
        {
            await _cache.SetTableAsync(table, cancellationToken);
        }

        return table;
    }

    public async Task<IReadOnlyList<TableMetadata>> ListTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Try cache first
        var cached = await _cache.GetTablesAsync(tenantId, cancellationToken);
        if (cached is not null)
        {
            CachingSchemaManagerLogs.TableListCacheHit(_logger, tenantId);
            return cached;
        }

        CachingSchemaManagerLogs.TableListCacheMiss(_logger, tenantId);

        // Fetch from database
        var tables = await _inner.ListTablesAsync(tenantId, cancellationToken);
        await _cache.SetTablesAsync(tenantId, tables, cancellationToken);

        return tables;
    }

    public async Task<TableMetadata> UpdateTableAsync(
        UpdateTableRequest request,
        CancellationToken cancellationToken = default)
    {
        // First get the existing table to know tenant and old name
        var existing = await _inner.GetTableByIdAsync(request.TableId, cancellationToken);

        var table = await _inner.UpdateTableAsync(request, cancellationToken);

        // Invalidate old entries and cache new
        await _cache.InvalidateTableAsync(request.TableId, cancellationToken);
        if (existing is not null)
        {
            await _cache.InvalidateTableAsync(existing.TenantId, existing.LogicalName, cancellationToken);
        }
        await _cache.SetTableAsync(table, cancellationToken);

        return table;
    }

    public async Task DeleteTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        // Get table info before deletion
        var table = await _inner.GetTableByIdAsync(tableId, cancellationToken);

        await _inner.DeleteTableAsync(tableId, cancellationToken);

        // Invalidate cache
        await _cache.InvalidateTableAsync(tableId, cancellationToken);
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
            await _cache.InvalidateTenantTablesAsync(table.TenantId, cancellationToken);
        }
    }

    public async Task<ColumnMetadata> AddColumnAsync(
        AddColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        var column = await _inner.AddColumnAsync(request, cancellationToken);

        // Invalidate both ID and name-based cache keys
        var table = await _inner.GetTableByIdAsync(request.TableId, cancellationToken);
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
        }
        else
        {
            await _cache.InvalidateTableAsync(request.TableId, cancellationToken);
        }

        return column;
    }

    public async Task<ColumnMetadata> UpdateColumnAsync(
        UpdateColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        var column = await _inner.UpdateColumnAsync(request, cancellationToken);

        // Get the table for this column and invalidate
        var table = await _inner.GetTableByIdAsync(column.TableId, cancellationToken);
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
        }

        return column;
    }

    public async Task<ColumnMetadata> RenameColumnAsync(
        RenameColumnRequest request,
        CancellationToken cancellationToken = default)
    {
        var column = await _inner.RenameColumnAsync(request, cancellationToken);

        // Invalidate table cache (column name changed affects schema)
        var table = await _inner.GetTableByIdAsync(column.TableId, cancellationToken);
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
        }

        return column;
    }

    public async Task DeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default)
    {
        // Find the owning table before deletion so we can invalidate cache
        var tables = await FindTableForColumnAsync(columnId, cancellationToken);

        await _inner.DeleteColumnAsync(columnId, cancellationToken);

        // Invalidate both ID and name-based cache keys
        if (tables is not null)
        {
            await _cache.InvalidateTableAsync(tables.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(tables.TenantId, tables.LogicalName, cancellationToken);
        }
    }

    public async Task<IndexMetadata> CreateIndexAsync(
        CreateIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var index = await _inner.CreateIndexAsync(request, cancellationToken);

        // Invalidate both ID and name-based cache keys
        var table = await _inner.GetTableByIdAsync(request.TableId, cancellationToken);
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
        }
        else
        {
            await _cache.InvalidateTableAsync(request.TableId, cancellationToken);
        }

        return index;
    }

    public async Task DeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default)
    {
        // Find the owning table before deletion so we can invalidate cache
        var table = await FindTableForIndexAsync(indexId, cancellationToken);

        await _inner.DeleteIndexAsync(indexId, cancellationToken);

        // Invalidate both ID and name-based cache keys
        if (table is not null)
        {
            await _cache.InvalidateTableAsync(table.TableId, cancellationToken);
            await _cache.InvalidateTableAsync(table.TenantId, table.LogicalName, cancellationToken);
        }
    }

    public async Task<RelationMetadata> CreateRelationAsync(
        CreateRelationRequest request,
        CancellationToken cancellationToken = default)
    {
        var relation = await _inner.CreateRelationAsync(request, cancellationToken);

        // Invalidate both source and target tables
        await _cache.InvalidateTableAsync(request.SourceTableId, cancellationToken);
        await _cache.InvalidateTableAsync(request.TargetTableId, cancellationToken);
        await _cache.InvalidateTenantTablesAsync(request.TenantId, cancellationToken);

        return relation;
    }

    public async Task DeleteRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken = default)
    {
        // Find the relation's tables before deletion so we can invalidate cache
        var relation = await FindRelationAsync(relationId, cancellationToken);

        await _inner.DeleteRelationAsync(relationId, cancellationToken);

        // Invalidate both source and target table caches
        if (relation is not null)
        {
            await _cache.InvalidateTableAsync(relation.SourceTableId, cancellationToken);
            await _cache.InvalidateTableAsync(relation.TargetTableId, cancellationToken);

            var sourceTable = await _inner.GetTableByIdAsync(relation.SourceTableId, cancellationToken);
            if (sourceTable is not null)
            {
                await _cache.InvalidateTableAsync(sourceTable.TenantId, sourceTable.LogicalName, cancellationToken);
            }

            var targetTable = await _inner.GetTableByIdAsync(relation.TargetTableId, cancellationToken);
            if (targetTable is not null)
            {
                await _cache.InvalidateTableAsync(targetTable.TenantId, targetTable.LogicalName, cancellationToken);
            }
        }
    }

    #region Cache Invalidation Helpers

    private async Task<TableMetadata?> FindTableForColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken)
    {
        var column = await _metadataRepository.GetColumnByIdAsync(columnId, cancellationToken);
        if (column is null) return null;
        return await _inner.GetTableByIdAsync(column.TableId, cancellationToken);
    }

    private async Task<TableMetadata?> FindTableForIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken)
    {
        var index = await _metadataRepository.GetIndexByIdAsync(indexId, cancellationToken);
        if (index is null) return null;
        return await _inner.GetTableByIdAsync(index.TableId, cancellationToken);
    }

    private async Task<RelationMetadata?> FindRelationAsync(
        Guid relationId,
        CancellationToken cancellationToken)
    {
        return await _metadataRepository.GetRelationByIdAsync(relationId, cancellationToken);
    }

    #endregion
}

internal static partial class CachingSchemaManagerLogs
{
    [LoggerMessage(LogLevel.Debug, "Cache hit for table '{TableName}'")]
    public static partial void CacheHit(ILogger logger, string tableName);

    [LoggerMessage(LogLevel.Debug, "Cache miss for table '{TableName}'")]
    public static partial void CacheMiss(ILogger logger, string tableName);

    [LoggerMessage(LogLevel.Debug, "Cache hit for table ID '{TableId}'")]
    public static partial void CacheHitById(ILogger logger, Guid tableId);

    [LoggerMessage(LogLevel.Debug, "Cache miss for table ID '{TableId}'")]
    public static partial void CacheMissById(ILogger logger, Guid tableId);

    [LoggerMessage(LogLevel.Debug, "Table list cache hit for tenant '{TenantId}'")]
    public static partial void TableListCacheHit(ILogger logger, Guid tenantId);

    [LoggerMessage(LogLevel.Debug, "Table list cache miss for tenant '{TenantId}'")]
    public static partial void TableListCacheMiss(ILogger logger, Guid tenantId);
}
