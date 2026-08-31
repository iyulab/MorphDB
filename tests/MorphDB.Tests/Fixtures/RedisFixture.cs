using StackExchange.Redis;
using Testcontainers.Redis;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Shared Redis container for tests that exercise the real <see cref="IConnectionMultiplexer"/>
/// path (schema cache invalidation), as opposed to the in-memory <c>IDistributedCache</c> double.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public IConnectionMultiplexer Multiplexer { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Multiplexer = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await Multiplexer.CloseAsync();
        Multiplexer.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisFixture>
{
}
