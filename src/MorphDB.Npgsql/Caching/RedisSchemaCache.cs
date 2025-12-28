using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Caching;

/// <summary>
/// Redis-based implementation of schema cache.
/// </summary>
public sealed class RedisSchemaCache : ISchemaCache
{
    private readonly IDistributedCache _cache;
    private readonly SchemaCacheOptions _options;
    private readonly ILogger<RedisSchemaCache> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisSchemaCache(
        IDistributedCache cache,
        IOptions<SchemaCacheOptions> options,
        ILogger<RedisSchemaCache> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TableMetadata?> GetTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var key = BuildTableKey(tenantId, logicalName);
        return await GetAsync<TableMetadata>(key, cancellationToken);
    }

    public async Task<TableMetadata?> GetTableByIdAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var key = BuildTableIdKey(tableId);
        return await GetAsync<TableMetadata>(key, cancellationToken);
    }

    public async Task<IReadOnlyList<TableMetadata>?> GetTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var key = BuildTenantTablesKey(tenantId);
        return await GetAsync<List<TableMetadata>>(key, cancellationToken);
    }

    public async Task SetTableAsync(
        TableMetadata table,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.TableCacheDuration
        };

        // Cache by tenant + logical name
        var nameKey = BuildTableKey(table.TenantId, table.LogicalName);
        await SetAsync(nameKey, table, cacheOptions, cancellationToken);

        // Cache by ID
        var idKey = BuildTableIdKey(table.TableId);
        await SetAsync(idKey, table, cacheOptions, cancellationToken);

        SchemaCacheLogs.TableCached(_logger, table.LogicalName, table.TableId);
    }

    public async Task SetTablesAsync(
        Guid tenantId,
        IReadOnlyList<TableMetadata> tables,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.TableListCacheDuration
        };

        var key = BuildTenantTablesKey(tenantId);
        await SetAsync(key, tables.ToList(), cacheOptions, cancellationToken);

        // Also cache individual tables
        foreach (var table in tables)
        {
            await SetTableAsync(table, cancellationToken);
        }

        SchemaCacheLogs.TableListCached(_logger, tenantId, tables.Count);
    }

    public async Task InvalidateTableAsync(
        Guid tableId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var idKey = BuildTableIdKey(tableId);
        await _cache.RemoveAsync(idKey, cancellationToken);

        SchemaCacheLogs.TableInvalidated(_logger, tableId);
    }

    public async Task InvalidateTableAsync(
        Guid tenantId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var nameKey = BuildTableKey(tenantId, logicalName);
        await _cache.RemoveAsync(nameKey, cancellationToken);

        // Also invalidate tenant table list
        await InvalidateTenantTablesAsync(tenantId, cancellationToken);

        SchemaCacheLogs.TableNameInvalidated(_logger, tenantId, logicalName);
    }

    public async Task InvalidateTenantTablesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var key = BuildTenantTablesKey(tenantId);
        await _cache.RemoveAsync(key, cancellationToken);

        SchemaCacheLogs.TenantTablesInvalidated(_logger, tenantId);
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        // Note: Redis doesn't have a simple "clear by prefix" operation
        // This would require using Redis SCAN + DEL or using a separate tracking mechanism
        // For now, we log that this operation is limited
        SchemaCacheLogs.InvalidateAllRequested(_logger);
        await Task.CompletedTask;
    }

    private string BuildTableKey(Guid tenantId, string logicalName) =>
        $"{_options.KeyPrefix}:table:{tenantId}:{logicalName.ToLowerInvariant()}";

    private string BuildTableIdKey(Guid tableId) =>
        $"{_options.KeyPrefix}:table:id:{tableId}";

    private string BuildTenantTablesKey(Guid tenantId) =>
        $"{_options.KeyPrefix}:tables:{tenantId}";

    private async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            if (bytes is null || bytes.Length == 0)
                return null;

            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex)
        {
            SchemaCacheLogs.CacheReadError(_logger, key, ex);
            return null;
        }
    }

    private async Task SetAsync<T>(
        string key,
        T value,
        DistributedCacheEntryOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await _cache.SetAsync(key, bytes, options, cancellationToken);
        }
        catch (Exception ex)
        {
            SchemaCacheLogs.CacheWriteError(_logger, key, ex);
        }
    }
}

internal static partial class SchemaCacheLogs
{
    [LoggerMessage(LogLevel.Debug, "Cached table '{TableName}' ({TableId})")]
    public static partial void TableCached(ILogger logger, string tableName, Guid tableId);

    [LoggerMessage(LogLevel.Debug, "Cached {Count} tables for tenant {TenantId}")]
    public static partial void TableListCached(ILogger logger, Guid tenantId, int count);

    [LoggerMessage(LogLevel.Debug, "Invalidated table cache for {TableId}")]
    public static partial void TableInvalidated(ILogger logger, Guid tableId);

    [LoggerMessage(LogLevel.Debug, "Invalidated table cache for {TenantId}/{LogicalName}")]
    public static partial void TableNameInvalidated(ILogger logger, Guid tenantId, string logicalName);

    [LoggerMessage(LogLevel.Debug, "Invalidated all tables for tenant {TenantId}")]
    public static partial void TenantTablesInvalidated(ILogger logger, Guid tenantId);

    [LoggerMessage(LogLevel.Warning, "InvalidateAll requested but not fully supported with Redis")]
    public static partial void InvalidateAllRequested(ILogger logger);

    [LoggerMessage(LogLevel.Warning, "Failed to read from cache key '{Key}'")]
    public static partial void CacheReadError(ILogger logger, string key, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Failed to write to cache key '{Key}'")]
    public static partial void CacheWriteError(ILogger logger, string key, Exception exception);
}
