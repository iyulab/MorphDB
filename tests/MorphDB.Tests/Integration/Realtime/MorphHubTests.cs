using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Realtime;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Realtime;

/// <summary>
/// Integration tests for MorphHub SignalR functionality.
/// </summary>
[Collection("API")]
[Trait("Category", "RealtimeIntegration")]
public sealed class MorphHubTests : IAsyncLifetime
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _httpClient;
    private HubConnection? _hubConnection;
    private readonly List<RecordChangedMessage> _receivedCreatedMessages = [];
    private readonly List<RecordChangedMessage> _receivedUpdatedMessages = [];
    private readonly List<RecordDeletedMessage> _receivedDeletedMessages = [];

    public MorphHubTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _httpClient = fixture.Api.Client;
    }

    public async ValueTask InitializeAsync()
    {
        var hubUrl = new Uri(_fixture.Api.BaseAddress, "hubs/morph").ToString();

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Project-Id", _fixture.Api.ProjectId.ToString());
                // Use the test server's handler to route SignalR requests through the test server
                options.HttpMessageHandlerFactory = _ => _fixture.Api.CreateHandler();
            })
            .Build();

        _hubConnection.On<RecordChangedMessage>("RecordCreated", msg => _receivedCreatedMessages.Add(msg));
        _hubConnection.On<RecordChangedMessage>("RecordUpdated", msg => _receivedUpdatedMessages.Add(msg));
        _hubConnection.On<RecordDeletedMessage>("RecordDeleted", msg => _receivedDeletedMessages.Add(msg));

        await _hubConnection.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }

    private async Task<string> SetupTestTableAsync()
    {
        var tableName = $"realtime_test_{Guid.NewGuid():N}"[..30];
        await _httpClient.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "value", Type = "integer", Nullable = true }
            ]
        });
        return tableName;
    }

    #region Connection Tests

    [Fact]
    public async Task Connect_ShouldEstablishConnection()
    {
        // Assert
        _hubConnection!.State.Should().Be(HubConnectionState.Connected);
    }

    /// <summary>
    /// A connection that names no project must be refused at connect. It used to fall back to
    /// <see cref="Guid.Empty"/>, where nothing publishes — the client subscribed successfully and then
    /// received nothing, forever, with no error to explain it.
    /// </summary>
    [Fact]
    public async Task Connect_WithoutProjectHeader_ShouldBeRefused()
    {
        await using var connection = BuildConnectionWithHeaders(new Dictionary<string, string>());

        await AssertRefusedAsync(connection);
    }

    /// <summary>
    /// <see cref="Guid.Empty"/> is not a project — it is the exact value the removed fallback used, so
    /// accepting it would reopen the silent-dead-subscription path under a header that looks valid.
    /// </summary>
    [Fact]
    public async Task Connect_WithEmptyGuidProjectHeader_ShouldBeRefused()
    {
        await using var connection = BuildConnectionWithHeaders(
            new Dictionary<string, string> { ["X-Project-Id"] = Guid.Empty.ToString() });

        await AssertRefusedAsync(connection);
    }

    /// <summary>
    /// The same malformed/missing distinction REST and GraphQL make on `X-Project-Id` must hold at
    /// connect time too — a header that was sent but does not parse is a different problem than a
    /// header that was never sent, and the two must not be reported identically here either.
    /// </summary>
    [Fact]
    public async Task Connect_WithMalformedProjectHeader_ShouldBeRefused()
    {
        await using var malformed = BuildConnectionWithHeaders(
            new Dictionary<string, string> { ["X-Project-Id"] = "not-a-guid" });
        await using var missing = BuildConnectionWithHeaders(new Dictionary<string, string>());

        var malformedMessage = await AssertRefusedAsync(malformed);
        var missingMessage = await AssertRefusedAsync(missing);

        malformedMessage.Should().NotBeNullOrEmpty(
            "EnableDetailedErrors is on, so the refusal must say why, the same as REST/GraphQL's 400 body does");
        malformedMessage.Should().Contain("GUID").And.Contain("not-a-guid",
            "a malformed header must be reported as MalformedProjectIdException, not the generic missing-project refusal");
        malformedMessage.Should().NotBe(missingMessage,
            "the two refusal reasons must be distinguishable, the same as MISSING_PROJECT vs INVALID_PROJECT_ID on REST");
    }

    private HubConnection BuildConnectionWithHeaders(IDictionary<string, string> headers)
    {
        var hubUrl = new Uri(_fixture.Api.BaseAddress, "hubs/morph").ToString();

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                foreach (var (name, value) in headers)
                {
                    options.Headers.Add(name, value);
                }
                options.HttpMessageHandlerFactory = _ => _fixture.Api.CreateHandler();
            })
            .Build();
    }

    /// <summary>
    /// The refusal can surface in either of two shapes depending on timing: the handshake may fail
    /// outright (StartAsync throws), or the handshake completes before OnConnectedAsync throws and the
    /// server then closes the connection. Both count; a connection that stays usable does not.
    /// <para>
    /// Returns whatever refusal text was observable on that shape (the caught exception's message, or
    /// the <see cref="HubConnection.Closed"/> handler's, when the close arrives after a completed
    /// handshake) — null if neither surfaced one, which a caller checking only connection state may
    /// ignore.
    /// </para>
    /// </summary>
    private static async Task<string?> AssertRefusedAsync(HubConnection connection)
    {
        string? closedMessage = null;
        connection.Closed += ex =>
        {
            closedMessage = ex?.Message;
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync();
        }
        catch (Exception ex)
        {
            connection.State.Should().Be(HubConnectionState.Disconnected);
            return ex.Message;
        }

        var deadline = Task.Delay(TimeSpan.FromSeconds(5));
        while (connection.State == HubConnectionState.Connected && !deadline.IsCompleted)
        {
            await Task.Delay(50);
        }

        connection.State.Should().Be(
            HubConnectionState.Disconnected,
            "a connection that does not name a project must not remain connected");

        return closedMessage;
    }

    #endregion

    #region Subscription Tests

    [Fact]
    public async Task Subscribe_ShouldAddToSubscriptionList()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions", TestContext.Current.CancellationToken);

        // Assert
        subscriptions.Should().Contain(tableName);
    }

    [Fact]
    public async Task Unsubscribe_ShouldRemoveFromSubscriptionList()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);

        // Act
        await _hubConnection!.InvokeAsync("Unsubscribe", tableName, TestContext.Current.CancellationToken);
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions", TestContext.Current.CancellationToken);

        // Assert
        subscriptions.Should().NotContain(tableName);
    }

    [Fact]
    public async Task SubscribeMany_ShouldAddMultipleTables()
    {
        // Arrange
        var table1 = await SetupTestTableAsync();
        var table2 = await SetupTestTableAsync();

        // Act
        await _hubConnection!.InvokeAsync("SubscribeMany", new[] { table1, table2 }, TestContext.Current.CancellationToken);
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions", TestContext.Current.CancellationToken);

        // Assert
        subscriptions.Should().Contain(table1);
        subscriptions.Should().Contain(table2);
    }

    #endregion

    #region Documented-call Tests

    /// <summary>
    /// The execution half of the real-time doc gate. Every other check on this surface compares one
    /// description to another — the documented method names to the hub's, the documented payload
    /// fields to the message's — and all of them stayed green while the one call the documentation
    /// tells a consumer to make was refused by the server before it reached the hub at all
    /// (<c>Invocation provides 1 argument(s) but target expects 2</c>: SignalR binds by argument
    /// count, and a C# default parameter is invisible on the wire).
    /// <para>
    /// The tests around this one did not catch it because they were written against the
    /// two-argument shape that works rather than the one-argument shape that is documented — the
    /// workaround got encoded as the expectation. This one invokes it exactly as
    /// <c>docs/API.md</c> writes it, so the documented path is the path under test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_subscribe_call_documented_for_clients_is_accepted_as_written()
    {
        var tableName = await SetupTestTableAsync();

        // Exactly `await connection.invoke("Subscribe", "customers");` — one argument, no options.
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);

        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions", TestContext.Current.CancellationToken);

        subscriptions.Should().Contain(tableName,
            "the documented call must not merely be accepted — it must actually subscribe");
    }

    #endregion

    #region Notification Tests

    [Fact]
    public async Task Subscribe_WhenRecordCreated_ShouldReceiveNotification()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedCreatedMessages.Clear();

        // Act
        var response = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        }, TestContext.Current.CancellationToken);

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        while (_receivedCreatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Assert
        _receivedCreatedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        _receivedCreatedMessages.First().Table.Should().Be(tableName);
        _receivedCreatedMessages.First().Operation.Should().Be("INSERT");
    }

    /// <summary>
    /// Regression: the broadcast used to carry the row exactly as the trigger's <c>to_jsonb(NEW)</c>
    /// produced it — physical column names and <c>project_id</c> included — because nothing on this
    /// path translated it, unlike REST/GraphQL/export/view.
    /// </summary>
    [Fact]
    public async Task Subscribe_WhenRecordCreated_NotificationDataUsesLogicalColumnNames()
    {
        var tableName = await SetupTestTableAsync();
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedCreatedMessages.Clear();

        await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        }, TestContext.Current.CancellationToken);

        var timeout = Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        while (_receivedCreatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        _receivedCreatedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        var data = _receivedCreatedMessages.First().Data;

        data.Keys.Should().Contain(["name", "value", "_id"],
            "the broadcast must speak the same logical vocabulary every other surface does");
        data.Keys.Should().NotContain("project_id",
            "the project is already the connection's scope; the internal GUID says nothing to a subscriber");
        data.Keys.Where(PhysicalNameGuard.IsPhysicalName).Should().BeEmpty(
            "no key in a real-time payload may be a physical (hash-based) column name");
    }

    /// <summary>
    /// Regression. The trigger used to put the whole row (<c>to_jsonb(NEW)</c>) into the NOTIFY
    /// payload, and PostgreSQL caps a payload at 8,000 bytes — raising inside the AFTER ROW trigger,
    /// which aborted the <em>write</em>: a <c>text</c> value of 9,000 characters answered <c>500</c>
    /// and was never stored, whether or not anyone was subscribed. The payload now carries only the
    /// row's key and the service reads the row back, so there is no size at which a write starts
    /// failing, and the subscriber still gets the whole row.
    /// </summary>
    [Fact]
    public async Task A_row_wider_than_a_notify_payload_is_stored_and_broadcast_whole()
    {
        var tableName = $"realtime_wide_{Guid.NewGuid():N}"[..30];
        await _httpClient.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "body", Type = "text", Nullable = false }]
        }, TestContext.Current.CancellationToken);
        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedCreatedMessages.Clear();

        var body = new string('x', 9_000);
        var response = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["body"] = body
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "a row's width must not decide whether it can be written — the notify payload carries the key, not the row");

        var timeout = Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        while (_receivedCreatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        _receivedCreatedMessages.Should().HaveCount(1);
        _receivedCreatedMessages[0].Data["body"]!.ToString().Should().Be(body,
            "the subscriber gets the row as REST would serve it, read back by key after the commit");
    }

    /// <summary>
    /// Regression. Notifications used to be handled inside Npgsql's event handler — an <c>async
    /// void</c> — so handlers for consecutive commits overlapped on their awaits and could broadcast
    /// in either order. PostgreSQL delivers NOTIFY in commit order, and a subscriber has nothing else
    /// to tell two changes to one row apart, so the service must keep that order: one consumer,
    /// one notification at a time.
    /// <para>
    /// Each change is read back by key when handled, so a later commit may already be visible when
    /// an earlier notification is handled — which is why the sequence is asserted non-decreasing
    /// rather than equal to <c>1..n</c>: order is the contract, per-statement images are not. The
    /// event's <c>timestamp</c> is not the order key either: the trigger stamps it with the
    /// transaction's start time, and PostgreSQL orders notifications by commit, so a transaction
    /// that started earlier and committed later is correctly delivered later with an earlier stamp.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Consecutive_changes_to_one_row_are_broadcast_in_commit_order()
    {
        const int changes = 20;
        var tableName = await SetupTestTableAsync();
        var createResponse = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Ordered",
            ["value"] = 0
        }, TestContext.Current.CancellationToken);
        var recordId = (await createResponse.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken))!.Id;

        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedUpdatedMessages.Clear();

        for (var i = 1; i <= changes; i++)
        {
            var patch = await _httpClient.PatchAsJsonAsync($"/api/data/{tableName}/{recordId}", new Dictionary<string, object?>
            {
                ["value"] = i
            }, TestContext.Current.CancellationToken);
            patch.EnsureSuccessStatusCode();
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        while (_receivedUpdatedMessages.Count < changes && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        _receivedUpdatedMessages.Should().OnlyContain(m => m.Table == tableName && m.RecordId == recordId,
            "the connection is subscribed to this table alone, so nothing else may reach it");
        _receivedUpdatedMessages.Should().HaveCount(changes, "every committed change is broadcast once");
        _receivedUpdatedMessages.Select(m => Convert.ToInt32(m.Data["value"]!.ToString(), CultureInfo.InvariantCulture)).Should().BeInAscendingOrder(
            "a row read back in handling order can only ever show a state at or after the notifying commit");
    }

    [Fact]
    public async Task Subscribe_WhenRecordUpdated_ShouldReceiveNotification()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Create a record first
        var createResponse = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        }, TestContext.Current.CancellationToken);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken);
        var recordId = createResult!.Id;

        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedUpdatedMessages.Clear();

        // Act
        await _httpClient.PatchAsJsonAsync($"/api/data/{tableName}/{recordId}", new Dictionary<string, object?>
        {
            ["value"] = 100
        }, TestContext.Current.CancellationToken);

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        while (_receivedUpdatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Assert
        _receivedUpdatedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        _receivedUpdatedMessages.First().Table.Should().Be(tableName);
        _receivedUpdatedMessages.First().Operation.Should().Be("UPDATE");
    }

    [Fact]
    public async Task Subscribe_WhenRecordDeleted_ShouldReceiveNotification()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Create a record first
        var createResponse = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        }, TestContext.Current.CancellationToken);
        var createResult = await createResponse.Content.ReadFromJsonAsync<DataRecordResponse>(TestContext.Current.CancellationToken);
        var recordId = createResult!.Id;

        await _hubConnection!.InvokeAsync("Subscribe", tableName, TestContext.Current.CancellationToken);
        _receivedDeletedMessages.Clear();

        // Act
        await _httpClient.DeleteAsync($"/api/data/{tableName}/{recordId}", TestContext.Current.CancellationToken);

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        while (_receivedDeletedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        // Assert
        _receivedDeletedMessages.Should().HaveCountGreaterThanOrEqualTo(1);
        _receivedDeletedMessages.First().Table.Should().Be(tableName);
        _receivedDeletedMessages.First().RecordId.Should().Be(recordId);
    }

    #endregion

    #region Isolation Tests

    [Fact]
    public async Task Subscribe_DifferentTable_ShouldNotReceiveNotifications()
    {
        // Arrange
        var subscribedTable = await SetupTestTableAsync();
        var otherTable = await SetupTestTableAsync();

        await _hubConnection!.InvokeAsync("Subscribe", subscribedTable, TestContext.Current.CancellationToken);
        _receivedCreatedMessages.Clear();

        // Act - Insert into other table
        await _httpClient.PostAsJsonAsync($"/api/data/{otherTable}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        }, TestContext.Current.CancellationToken);

        // Wait a bit
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Assert - Should not receive notification for other table
        _receivedCreatedMessages.Where(m => m.Table == otherTable).Should().BeEmpty();
    }

    #endregion
}

/// <summary>
/// Response model for data record creation.
/// </summary>
file sealed class DataRecordResponse
{
    public Guid Id { get; init; }
}
