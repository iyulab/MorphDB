using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Caching;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Exercises <see cref="RedisSchemaCache"/> against a real Redis, because
/// <see cref="ISchemaCache.InvalidateAllAsync"/> is exactly the operation an in-memory double
/// cannot honestly stand in for: it used to log a warning and delete nothing.
/// </summary>
[Collection("Redis")]
public class RedisSchemaCacheTests
{
    private readonly RedisFixture _fixture;

    public RedisSchemaCacheTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    private RedisSchemaCache CreateCache(string keyPrefix)
    {
        var distributedCache = new RedisCache(new RedisCacheOptions
        {
            Configuration = _fixture.ConnectionString,
            InstanceName = keyPrefix + ":",
        });

        var options = Options.Create(new SchemaCacheOptions
        {
            Enabled = true,
            KeyPrefix = keyPrefix,
        });

        return new RedisSchemaCache(distributedCache, _fixture.Multiplexer, options, NullLogger<RedisSchemaCache>.Instance);
    }

    private static TableMetadata MakeTable(Guid projectId, string logicalName) => new()
    {
        TableId = Guid.NewGuid(),
        ProjectId = projectId,
        LogicalName = logicalName,
        PhysicalName = $"t_{logicalName}",
    };

    [Fact]
    public async Task InvalidateAllAsync_actually_removes_every_cached_entry()
    {
        var keyPrefix = $"morphdb:test:{Guid.NewGuid():N}";
        var cache = CreateCache(keyPrefix);

        var projectId = Guid.NewGuid();
        var tableA = MakeTable(projectId, "orders");
        var tableB = MakeTable(projectId, "customers");

        await cache.SetTableAsync(tableA);
        await cache.SetTableAsync(tableB);

        (await cache.GetTableAsync(projectId, "orders")).Should().NotBeNull(
            "the cache write above must be visible before invalidation is exercised");
        (await cache.GetTableAsync(projectId, "customers")).Should().NotBeNull();

        await cache.InvalidateAllAsync();

        (await cache.GetTableAsync(projectId, "orders")).Should().BeNull(
            "InvalidateAllAsync used to just log a warning (SchemaCacheLogs.InvalidateAllRequested) and delete nothing");
        (await cache.GetTableAsync(projectId, "customers")).Should().BeNull();
        (await cache.GetTableByIdAsync(tableA.TableId)).Should().BeNull(
            "SetTableAsync writes a second id-keyed entry per table; invalidate-all must clear both");
        (await cache.GetTablesAsync(projectId)).Should().BeNull();
    }

    [Fact]
    public async Task InvalidateAllAsync_does_not_touch_keys_outside_its_prefix()
    {
        var keyPrefixA = $"morphdb:test:{Guid.NewGuid():N}";
        var keyPrefixB = $"morphdb:test:{Guid.NewGuid():N}";
        var cacheA = CreateCache(keyPrefixA);
        var cacheB = CreateCache(keyPrefixB);

        var projectId = Guid.NewGuid();
        await cacheA.SetTableAsync(MakeTable(projectId, "orders"));
        await cacheB.SetTableAsync(MakeTable(projectId, "orders"));

        await cacheA.InvalidateAllAsync();

        (await cacheA.GetTableAsync(projectId, "orders")).Should().BeNull();
        (await cacheB.GetTableAsync(projectId, "orders")).Should().NotBeNull(
            "a differently-prefixed cache instance shares the same Redis but not the same namespace");
    }
}
