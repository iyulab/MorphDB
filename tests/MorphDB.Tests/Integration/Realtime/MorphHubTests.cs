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
public class MorphHubTests : IAsyncLifetime
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

    public async Task InitializeAsync()
    {
        var hubUrl = _httpClient.BaseAddress + "hubs/morph";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Tenant-Id", _fixture.Api.TenantId.ToString());
                options.HttpMessageHandlerFactory = _ => _fixture.Api.CreateClientWithTenant(_fixture.Api.TenantId)
                    .GetType()
                    .GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .GetValue(_fixture.Api.CreateClientWithTenant(_fixture.Api.TenantId)) as HttpMessageHandler
                    ?? new HttpClientHandler();
            })
            .Build();

        _hubConnection.On<RecordChangedMessage>("RecordCreated", msg => _receivedCreatedMessages.Add(msg));
        _hubConnection.On<RecordChangedMessage>("RecordUpdated", msg => _receivedUpdatedMessages.Add(msg));
        _hubConnection.On<RecordDeletedMessage>("RecordDeleted", msg => _receivedDeletedMessages.Add(msg));

        await _hubConnection.StartAsync();
    }

    public async Task DisposeAsync()
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

    #endregion

    #region Subscription Tests

    [Fact]
    public async Task Subscribe_ShouldAddToSubscriptionList()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();

        // Act
        await _hubConnection!.InvokeAsync("Subscribe", tableName, null);
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions");

        // Assert
        subscriptions.Should().Contain(tableName);
    }

    [Fact]
    public async Task Unsubscribe_ShouldRemoveFromSubscriptionList()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await _hubConnection!.InvokeAsync("Subscribe", tableName, null);

        // Act
        await _hubConnection!.InvokeAsync("Unsubscribe", tableName);
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions");

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
        await _hubConnection!.InvokeAsync("SubscribeMany", new[] { table1, table2 });
        var subscriptions = await _hubConnection!.InvokeAsync<IReadOnlyList<string>>("GetSubscriptions");

        // Assert
        subscriptions.Should().Contain(table1);
        subscriptions.Should().Contain(table2);
    }

    #endregion

    #region Notification Tests

    [Fact]
    public async Task Subscribe_WhenRecordCreated_ShouldReceiveNotification()
    {
        // Arrange
        var tableName = await SetupTestTableAsync();
        await _hubConnection!.InvokeAsync("Subscribe", tableName, null);
        _receivedCreatedMessages.Clear();

        // Act
        var response = await _httpClient.PostAsJsonAsync($"/api/data/{tableName}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        });

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (_receivedCreatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100);
        }

        // Assert
        _receivedCreatedMessages.Should().HaveCountGreaterOrEqualTo(1);
        _receivedCreatedMessages.First().Table.Should().Be(tableName);
        _receivedCreatedMessages.First().Operation.Should().Be("INSERT");
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
        });
        var createResult = await createResponse.Content.ReadFromJsonAsync<DataRecordResponse>();
        var recordId = createResult!.Id;

        await _hubConnection!.InvokeAsync("Subscribe", tableName, null);
        _receivedUpdatedMessages.Clear();

        // Act
        await _httpClient.PatchAsJsonAsync($"/api/data/{tableName}/{recordId}", new Dictionary<string, object?>
        {
            ["value"] = 100
        });

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (_receivedUpdatedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100);
        }

        // Assert
        _receivedUpdatedMessages.Should().HaveCountGreaterOrEqualTo(1);
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
        });
        var createResult = await createResponse.Content.ReadFromJsonAsync<DataRecordResponse>();
        var recordId = createResult!.Id;

        await _hubConnection!.InvokeAsync("Subscribe", tableName, null);
        _receivedDeletedMessages.Clear();

        // Act
        await _httpClient.DeleteAsync($"/api/data/{tableName}/{recordId}");

        // Wait for notification (with timeout)
        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        while (_receivedDeletedMessages.Count == 0 && !timeout.IsCompleted)
        {
            await Task.Delay(100);
        }

        // Assert
        _receivedDeletedMessages.Should().HaveCountGreaterOrEqualTo(1);
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

        await _hubConnection!.InvokeAsync("Subscribe", subscribedTable, null);
        _receivedCreatedMessages.Clear();

        // Act - Insert into other table
        await _httpClient.PostAsJsonAsync($"/api/data/{otherTable}", new Dictionary<string, object?>
        {
            ["name"] = "Test",
            ["value"] = 42
        });

        // Wait a bit
        await Task.Delay(TimeSpan.FromSeconds(1));

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
