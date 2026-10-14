using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A DDL lock is held until its handle is disposed. It used to be a transaction-level lock taken
/// outside a transaction, which PostgreSQL released at the end of the statement that took it: every
/// acquisition succeeded at once, and two schema changes to one table were never serialized.
/// </summary>
[Collection("PostgreSQL")]
public class AdvisoryLockManagerTests
{
    private readonly PostgresAdvisoryLockManager _locks;

    public AdvisoryLockManagerTests(PostgresFixture fixture)
    {
        _locks = new PostgresAdvisoryLockManager(
            fixture.Connections, new AdvisoryLockOptions { RetryInterval = TimeSpan.FromMilliseconds(50) });
    }

    [Fact]
    public async Task A_held_lock_is_not_taken_again_until_its_handle_is_disposed()
    {
        var resource = $"table:{Guid.NewGuid()}";

        var (first, held) = await _locks.TryAcquireDdlLockAsync(resource, TestContext.Current.CancellationToken);
        first.Should().BeTrue();

        var (second, notHeld) = await _locks.TryAcquireDdlLockAsync(resource, TestContext.Current.CancellationToken);
        second.Should().BeFalse("the first handle still holds the lock");
        notHeld.Should().BeNull();

        await held!.DisposeAsync();

        var (third, again) = await _locks.TryAcquireDdlLockAsync(resource, TestContext.Current.CancellationToken);
        third.Should().BeTrue("disposing the first handle released the lock");
        await again!.DisposeAsync();
    }

    [Fact]
    public async Task Acquiring_a_held_lock_waits_and_then_gives_up_with_the_resource_named()
    {
        var resource = $"table:{Guid.NewGuid()}";
        await using var held = await _locks.AcquireDdlLockAsync(resource, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var waiting = () => _locks.AcquireDdlLockAsync(resource, TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);

        (await waiting.Should().ThrowAsync<LockAcquisitionException>()).Which.Message.Should().Contain(resource);
    }

    [Fact]
    public async Task A_waiting_acquisition_gets_the_lock_once_the_holder_lets_go()
    {
        var resource = $"table:{Guid.NewGuid()}";
        var held = await _locks.AcquireDdlLockAsync(resource, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var waiting = _locks.AcquireDdlLockAsync(resource, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        waiting.IsCompleted.Should().BeFalse("the lock is still held");

        await held.DisposeAsync();
        await using var acquired = await waiting;
    }

    [Fact]
    public async Task Different_resources_do_not_wait_for_each_other()
    {
        await using var a = await _locks.AcquireDdlLockAsync($"table:{Guid.NewGuid()}", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var (acquired, b) = await _locks.TryAcquireDdlLockAsync($"table:{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        acquired.Should().BeTrue();
        await b!.DisposeAsync();
    }
}
