using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for real-time subscriptions.
/// </summary>
public sealed class RealtimeClient : IAsyncDisposable
{
    private readonly string _baseUrl;
    private readonly MorphDBClientOptions _options;
    private HubConnection? _connection;
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    internal RealtimeClient(string baseUrl, MorphDBClientOptions options)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _options = options;
    }

    /// <summary>
    /// Whether the client is connected.
    /// </summary>
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Connects to the real-time hub.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection != null && _connection.State == HubConnectionState.Connected)
                return;

            var hubUrl = $"{_baseUrl}/hubs/morph";

            var builder = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Headers["X-Project-Id"] = _options.ProjectId.ToString();
                })
                .WithAutomaticReconnect();

            _connection = builder.Build();

            _connection.On<string, string, string>("ReceiveChange", (tableName, operation, dataJson) =>
            {
                if (_subscriptions.TryGetValue(tableName, out var subscription))
                {
                    var changeOp = operation.ToLowerInvariant() switch
                    {
                        "insert" => ChangeOperation.Insert,
                        "update" => ChangeOperation.Update,
                        "delete" => ChangeOperation.Delete,
                        _ => ChangeOperation.Insert
                    };

                    var data = string.IsNullOrEmpty(dataJson)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, object?>>(dataJson, MorphDBJson.Options);

                    var notification = new ChangeNotification
                    {
                        TableName = tableName,
                        Operation = changeOp,
                        RecordId = data?.TryGetValue("id", out var id) == true && id is JsonElement je && je.TryGetGuid(out var guid)
                            ? guid : Guid.Empty,
                        Data = data,
                        ProjectId = _options.ProjectId,
                        Timestamp = DateTimeOffset.UtcNow
                    };

                    subscription.OnChange?.Invoke(notification);
                }
            });

            await _connection.StartAsync(cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Subscribes to changes on a table.
    /// </summary>
    public async Task<ISubscription> SubscribeAsync(
        string tableName,
        Action<ChangeNotification> onChangeHandler,
        CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken);

        if (_connection == null)
            throw new MorphDBConnectionException("Failed to connect to real-time hub");

        var subscriptionId = Guid.NewGuid().ToString();
        var subscription = new Subscription(subscriptionId, tableName, onChangeHandler, this);

        _subscriptions[tableName] = subscription;

        await _connection.InvokeAsync("Subscribe", tableName, cancellationToken);

        return subscription;
    }

    /// <summary>
    /// Unsubscribes from a table.
    /// </summary>
    internal async Task UnsubscribeAsync(string tableName, CancellationToken cancellationToken = default)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("Unsubscribe", tableName, cancellationToken);
        }

        _subscriptions.Remove(tableName);
    }

    /// <summary>
    /// Disconnects from the real-time hub.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            await _connection.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
            _connection = null;
        }

        _subscriptions.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectionLock.Dispose();
    }

    private sealed class Subscription : ISubscription
    {
        private readonly RealtimeClient _client;
        private bool _isActive = true;

        public Subscription(string subscriptionId, string tableName, Action<ChangeNotification> onChangeHandler, RealtimeClient client)
        {
            SubscriptionId = subscriptionId;
            TableName = tableName;
            OnChange = onChangeHandler;
            _client = client;
        }

        public string SubscriptionId { get; }
        public string TableName { get; }
        public bool IsActive => _isActive;
        public Action<ChangeNotification>? OnChange { get; }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (_isActive)
            {
                await _client.UnsubscribeAsync(TableName, cancellationToken);
                _isActive = false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await UnsubscribeAsync();
        }
    }
}
