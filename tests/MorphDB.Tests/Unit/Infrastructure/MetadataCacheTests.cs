using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit.Infrastructure;

/// <summary>
/// Holds what the definition cache may serve: nothing until something keeps it current, nothing it was
/// told is stale, and nothing read while it was being invalidated.
/// </summary>
public class MetadataCacheTests
{
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();

    private static TableMetadata Table(Guid project, string name) => new()
    {
        TableId = Guid.NewGuid(),
        ProjectId = project,
        LogicalName = name,
        PhysicalName = "tbl_" + name,
    };

    [Fact]
    public void Before_it_is_started_nothing_is_kept_or_served()
    {
        var cache = new MetadataCache();
        var orders = Table(ProjectA, "orders");

        cache.Keep(orders, cache.Generation);

        Assert.False(cache.IsLive);
        Assert.False(cache.TryGet(ProjectA, "orders", out _));
        Assert.False(cache.TryGet(orders.TableId, out _));
    }

    [Fact]
    public void Started_it_serves_a_kept_definition_by_name_and_by_id()
    {
        var cache = new MetadataCache();
        cache.Start();
        var orders = Table(ProjectA, "orders");

        cache.Keep(orders, cache.Generation);

        Assert.True(cache.TryGet(ProjectA, "orders", out var byName));
        Assert.Same(orders, byName);
        Assert.True(cache.TryGet(orders.TableId, out var byId));
        Assert.Same(orders, byId);
        Assert.False(cache.TryGet(ProjectB, "orders", out _));
    }

    [Fact]
    public void A_definition_read_while_the_cache_was_invalidated_is_not_kept()
    {
        var cache = new MetadataCache();
        cache.Start();
        var generation = cache.Generation;

        cache.Invalidate(ProjectB); // a change lands while the read is in flight
        cache.Keep(Table(ProjectA, "orders"), generation);

        Assert.False(cache.TryGet(ProjectA, "orders", out _));
    }

    [Fact]
    public void Invalidating_a_project_forgets_its_tables_and_keeps_the_others()
    {
        var cache = new MetadataCache();
        cache.Start();
        var orders = Table(ProjectA, "orders");
        var people = Table(ProjectB, "people");
        cache.Keep(orders, cache.Generation);
        cache.Keep(people, cache.Generation);

        cache.Invalidate(ProjectA);

        Assert.False(cache.TryGet(ProjectA, "orders", out _));
        Assert.False(cache.TryGet(orders.TableId, out _));
        Assert.True(cache.TryGet(ProjectB, "people", out _));
    }

    [Fact]
    public void Invalidating_with_no_project_forgets_everything()
    {
        var cache = new MetadataCache();
        cache.Start();
        cache.Keep(Table(ProjectA, "orders"), cache.Generation);
        cache.Keep(Table(ProjectB, "people"), cache.Generation);

        cache.Invalidate();

        Assert.False(cache.TryGet(ProjectA, "orders", out _));
        Assert.False(cache.TryGet(ProjectB, "people", out _));
    }

    [Fact]
    public void Stopped_it_forgets_and_serves_nothing_and_a_restart_begins_empty()
    {
        var cache = new MetadataCache();
        cache.Start();
        cache.Keep(Table(ProjectA, "orders"), cache.Generation);

        cache.Stop();
        Assert.False(cache.IsLive);
        Assert.False(cache.TryGet(ProjectA, "orders", out _));

        cache.Start();
        Assert.False(cache.TryGet(ProjectA, "orders", out _));
    }
}
