using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Caching;

/// <summary>
/// Decorator that adds caching to ISchemaManager operations.
/// </summary>
public sealed class CachingSchemaManagerDecorator : ISchemaManager
{
    private readonly ISchemaManager _inner;
    private readonly ISchemaCache _cache;
    private readonly ILogger<CachingSchemaManagerDecorator> _logger;

    public CachingSchemaManagerDecorator(
        ISchemaManager inner,
        ISchemaCache cache,
        ILogger<CachingSchemaManagerDecorator> logger)
    {
        _inner = inner;
        _cache = cache;
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

        // Invalidate table cache (columns changed)
        await _cache.InvalidateTableAsync(request.TableId, cancellationToken);

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

    public async Task DeleteColumnAsync(
        Guid columnId,
        CancellationToken cancellationToken = default)
    {
        await _inner.DeleteColumnAsync(columnId, cancellationToken);

        // Note: We don't have the table ID here easily, so we can't invalidate precisely
        // This is a limitation - consider passing tableId in the request
    }

    public async Task<IndexMetadata> CreateIndexAsync(
        CreateIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var index = await _inner.CreateIndexAsync(request, cancellationToken);

        // Invalidate table cache
        await _cache.InvalidateTableAsync(request.TableId, cancellationToken);

        return index;
    }

    public async Task DeleteIndexAsync(
        Guid indexId,
        CancellationToken cancellationToken = default)
    {
        await _inner.DeleteIndexAsync(indexId, cancellationToken);
        // Similar limitation as DeleteColumnAsync
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
        await _inner.DeleteRelationAsync(relationId, cancellationToken);
        // Similar limitation as DeleteColumnAsync
    }
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
