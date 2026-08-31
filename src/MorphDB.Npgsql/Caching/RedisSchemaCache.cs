using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using StackExchange.Redis;

namespace MorphDB.Npgsql.Caching;

/// <summary>
/// Redis-based implementation of schema cache.
/// </summary>
public sealed class RedisSchemaCache : ISchemaCache
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly SchemaCacheOptions _options;
    private readonly ILogger<RedisSchemaCache> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisSchemaCache(
        IDistributedCache cache,
        IConnectionMultiplexer multiplexer,
        IOptions<SchemaCacheOptions> options,
        ILogger<RedisSchemaCache> logger)
    {
        _cache = cache;
        _multiplexer = multiplexer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TableMetadata?> GetTableAsync(
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var key = BuildTableKey(projectId, logicalName);
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
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return null;

        var key = BuildProjectTablesKey(projectId);
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

        // Cache by project + logical name
        var nameKey = BuildTableKey(table.ProjectId, table.LogicalName);
        await SetAsync(nameKey, table, cacheOptions, cancellationToken);

        // Cache by ID
        var idKey = BuildTableIdKey(table.TableId);
        await SetAsync(idKey, table, cacheOptions, cancellationToken);

        SchemaCacheLogs.TableCached(_logger, table.LogicalName, table.TableId);
    }

    public async Task SetTablesAsync(
        Guid projectId,
        IReadOnlyList<TableMetadata> tables,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.TableListCacheDuration
        };

        var key = BuildProjectTablesKey(projectId);
        await SetAsync(key, tables.ToList(), cacheOptions, cancellationToken);

        // Also cache individual tables
        foreach (var table in tables)
        {
            await SetTableAsync(table, cancellationToken);
        }

        SchemaCacheLogs.TableListCached(_logger, projectId, tables.Count);
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
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var nameKey = BuildTableKey(projectId, logicalName);
        await _cache.RemoveAsync(nameKey, cancellationToken);

        // Also invalidate project table list
        await InvalidateProjectTablesAsync(projectId, cancellationToken);

        SchemaCacheLogs.TableNameInvalidated(_logger, projectId, logicalName);
    }

    public async Task InvalidateProjectTablesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        var key = BuildProjectTablesKey(projectId);
        await _cache.RemoveAsync(key, cancellationToken);

        SchemaCacheLogs.ProjectTablesInvalidated(_logger, projectId);
    }

    public async Task InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        // IDistributedCache has no "clear by prefix" primitive, so this drops to the raw
        // multiplexer and SCANs (via IServer.Keys, which is cursor-based under the hood, not
        // a blocking KEYS call) for every key under this cache's namespace, then deletes them
        // in one batch. The wildcard is on both sides because AddStackExchangeRedisCache
        // prepends its own InstanceName ahead of the KeyPrefix we already embed in each key
        // (see Build*Key below), so the on-the-wire key is not anchored at KeyPrefix.
        var pattern = (RedisValue)$"*{_options.KeyPrefix}*";
        var deletedCount = 0;

        foreach (var endpoint in _multiplexer.GetEndPoints())
        {
            var server = _multiplexer.GetServer(endpoint);
            if (server.IsReplica)
                continue;

            var db = _multiplexer.GetDatabase();
            var keys = server.KeysAsync(pattern: pattern);

            var batch = new List<RedisKey>();
            await foreach (var key in keys.WithCancellation(cancellationToken))
            {
                batch.Add(key);
            }

            if (batch.Count > 0)
            {
                deletedCount += (int)await db.KeyDeleteAsync([.. batch]);
            }
        }

        SchemaCacheLogs.InvalidateAllCompleted(_logger, deletedCount);
    }

    private string BuildTableKey(Guid projectId, string logicalName) =>
        $"{_options.KeyPrefix}:table:{projectId}:{logicalName.ToLowerInvariant()}";

    private string BuildTableIdKey(Guid tableId) =>
        $"{_options.KeyPrefix}:table:id:{tableId}";

    private string BuildProjectTablesKey(Guid projectId) =>
        $"{_options.KeyPrefix}:tables:{projectId}";

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

    [LoggerMessage(LogLevel.Debug, "Cached {Count} tables for project {ProjectId}")]
    public static partial void TableListCached(ILogger logger, Guid projectId, int count);

    [LoggerMessage(LogLevel.Debug, "Invalidated table cache for {TableId}")]
    public static partial void TableInvalidated(ILogger logger, Guid tableId);

    [LoggerMessage(LogLevel.Debug, "Invalidated table cache for {ProjectId}/{LogicalName}")]
    public static partial void TableNameInvalidated(ILogger logger, Guid projectId, string logicalName);

    [LoggerMessage(LogLevel.Debug, "Invalidated all tables for project {ProjectId}")]
    public static partial void ProjectTablesInvalidated(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "InvalidateAll removed {DeletedCount} schema cache keys")]
    public static partial void InvalidateAllCompleted(ILogger logger, int deletedCount);

    [LoggerMessage(LogLevel.Warning, "Failed to read from cache key '{Key}'")]
    public static partial void CacheReadError(ILogger logger, string key, Exception exception);

    [LoggerMessage(LogLevel.Warning, "Failed to write to cache key '{Key}'")]
    public static partial void CacheWriteError(ILogger logger, string key, Exception exception);
}
