using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// Drives the supported client's real-time surface against a running server, end to end: subscribe
/// through <see cref="MorphDBClient.Realtime"/>, change a row through the same client, and hold the
/// callback to what arrives.
/// <para>
/// Regression. The client used to listen for a <c>ReceiveChange</c> event carrying three strings,
/// which the hub had never sent — it broadcasts <c>RecordCreated</c> / <c>RecordUpdated</c> /
/// <c>RecordDeleted</c>, one message object each. <c>Subscribe</c> succeeded and then nothing
/// arrived, with no error, and no test drove this path, so nothing turned red. The guarantees
/// <c>docs/TESTING.md</c> holds across every server door have to hold across the client's door too.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "RealtimeIntegration")]
public sealed class RealtimeClientTests
{
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(15);

    private readonly ApiIntegrationFixture _fixture;

    public RealtimeClientTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private MorphDBClient ClientScopedToTheFixtureProject() =>
        new(_fixture.Api.BaseAddress.ToString(), new MorphDBClientOptions
        {
            ProjectId = _fixture.Api.ProjectId,
            HttpMessageHandler = _fixture.Api.CreateHandler(),
        });

    private static async Task<string> CreateTableAsync(MorphDBClient client)
    {
        var tableName = $"rt_client_{Guid.NewGuid():N}"[..30];
        await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnRequest { Name = "value", Type = "integer", Nullable = true },
            ],
        }, TestContext.Current.CancellationToken);
        return tableName;
    }

    private static async Task<ChangeNotification> NextAsync(Queue<ChangeNotification> received)
    {
        var deadline = DateTimeOffset.UtcNow + NotificationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (received)
            {
                if (received.Count > 0)
                    return received.Dequeue();
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"no change notification reached the client's callback within {NotificationTimeout} — "
            + "the client is listening for something the hub does not send");
    }

    [Fact]
    public async Task An_insert_reaches_the_subscriber_with_the_row_it_created()
    {
        await using var client = ClientScopedToTheFixtureProject();
        var tableName = await CreateTableAsync(client);
        var received = new Queue<ChangeNotification>();

        await using var subscription = await client.Realtime.SubscribeAsync(tableName, change =>
        {
            lock (received)
                received.Enqueue(change);
        }, TestContext.Current.CancellationToken);

        var created = await client.Data.InsertAsync(tableName, new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42,
        }, TestContext.Current.CancellationToken);

        var change = await NextAsync(received);

        change.TableName.Should().Be(tableName);
        change.Operation.Should().Be(ChangeOperation.Insert);
        change.RecordId.Should().Be(created.Id, "the id the server assigned rides on the event");
        change.ProjectId.Should().Be(_fixture.Api.ProjectId);
        change.Timestamp.Should().NotBe(default, "the event carries the server's timestamp");
        change.Data.Should().NotBeNull();
        change.Data!.Keys.Should().Contain(["name", "value", "_id"],
            "the client passes the logical vocabulary through as the hub sends it");
        change.Data["name"].Should().Be("Test",
            "values arrive as .NET values, the same as every REST response this client returns");
    }

    [Fact]
    public async Task An_update_and_a_delete_reach_the_subscriber_as_the_change_kinds_they_are()
    {
        await using var client = ClientScopedToTheFixtureProject();
        var tableName = await CreateTableAsync(client);
        var received = new Queue<ChangeNotification>();

        await using var subscription = await client.Realtime.SubscribeAsync(tableName, change =>
        {
            lock (received)
                received.Enqueue(change);
        }, TestContext.Current.CancellationToken);

        var created = await client.Data.InsertAsync(tableName, new Dictionary<string, object?> { ["name"] = "Before" },
            TestContext.Current.CancellationToken);
        (await NextAsync(received)).Operation.Should().Be(ChangeOperation.Insert);

        await client.Data.UpdateAsync(tableName, created.Id, new Dictionary<string, object?> { ["name"] = "After" },
            TestContext.Current.CancellationToken);
        var updated = await NextAsync(received);
        updated.Operation.Should().Be(ChangeOperation.Update);
        updated.RecordId.Should().Be(created.Id);
        updated.Data!["name"].Should().Be("After", "an update carries the row as it is now");

        await client.Data.DeleteAsync(tableName, created.Id, TestContext.Current.CancellationToken);
        var deleted = await NextAsync(received);
        deleted.Operation.Should().Be(ChangeOperation.Delete);
        deleted.RecordId.Should().Be(created.Id);
        deleted.Data.Should().BeNull("a deletion names the row that is gone and carries no data");
    }

    /// <summary>
    /// Two parts of one application may each want to watch the same table. The hub subscribes a
    /// connection to a table once, so the client has to fan the event out itself — and one
    /// subscriber leaving must not take the table away from the other. It used to: the second
    /// subscriber replaced the first in a dictionary keyed by table, silently.
    /// </summary>
    [Fact]
    public async Task Two_subscribers_on_one_table_both_hear_it_and_one_leaving_does_not_silence_the_other()
    {
        await using var client = ClientScopedToTheFixtureProject();
        var tableName = await CreateTableAsync(client);
        var first = new Queue<ChangeNotification>();
        var second = new Queue<ChangeNotification>();

        var firstSubscription = await client.Realtime.SubscribeAsync(tableName, change =>
        {
            lock (first)
                first.Enqueue(change);
        }, TestContext.Current.CancellationToken);
        await using var secondSubscription = await client.Realtime.SubscribeAsync(tableName, change =>
        {
            lock (second)
                second.Enqueue(change);
        }, TestContext.Current.CancellationToken);

        await client.Data.InsertAsync(tableName, new Dictionary<string, object?> { ["name"] = "Both" },
            TestContext.Current.CancellationToken);
        (await NextAsync(first)).Data!["name"].Should().Be("Both");
        (await NextAsync(second)).Data!["name"].Should().Be("Both");

        await firstSubscription.UnsubscribeAsync(TestContext.Current.CancellationToken);
        firstSubscription.IsActive.Should().BeFalse();

        await client.Data.InsertAsync(tableName, new Dictionary<string, object?> { ["name"] = "Second only" },
            TestContext.Current.CancellationToken);
        (await NextAsync(second)).Data!["name"].Should().Be("Second only",
            "the remaining subscriber still holds the table");
        lock (first)
        {
            first.Should().BeEmpty("a subscriber that left hears nothing more");
        }
    }

    /// <summary>
    /// The hub connection borrows the client's <see cref="MorphDBClientOptions.HttpMessageHandler"/>
    /// — that is what lets it reach a proxy, or this test server, at all — and disposes what it is
    /// handed when it closes. The handler is the client's, so closing the real-time connection must
    /// not take the client's REST calls down with it.
    /// </summary>
    [Fact]
    public async Task Disconnecting_the_hub_leaves_the_clients_http_handler_usable()
    {
        await using var client = ClientScopedToTheFixtureProject();
        var tableName = await CreateTableAsync(client);

        await client.Realtime.ConnectAsync(TestContext.Current.CancellationToken);
        client.Realtime.IsConnected.Should().BeTrue();
        await client.Realtime.DisconnectAsync(TestContext.Current.CancellationToken);

        var act = () => client.Schema.DropTableAsync(tableName, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
