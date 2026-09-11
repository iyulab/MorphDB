using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for real-time subscriptions.
/// <para>
/// The hub speaks three events, one per change kind — <c>RecordCreated</c>, <c>RecordUpdated</c>,
/// <c>RecordDeleted</c> — each carrying a single message object, and this client listens for exactly
/// those. It used to listen for a <c>ReceiveChange</c> event carrying three strings, which the server
/// had never sent: <c>Subscribe</c> succeeded and then nothing arrived, with no error to explain it.
/// The names registered here are held to the hub's client interface by a test, because the compiler
/// cannot see across the wire.
/// </para>
/// </summary>
public sealed class RealtimeClient : IAsyncDisposable
{
    private readonly string _baseUrl;
    private readonly MorphDBClientOptions _options;
    private HubConnection? _connection;
    private readonly Dictionary<string, List<Subscription>> _subscriptions = new();
    private readonly object _subscriptionsGate = new();
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

                    // The same handler the REST calls go through — a proxy, or a test server. The
                    // connection disposes whatever the factory hands it together with its own
                    // HttpClient, and the handler is owned by the MorphDBClient that outlives this
                    // connection, so it goes in behind a wrapper that does not pass disposal on.
                    if (_options.HttpMessageHandler is { } handler)
                        options.HttpMessageHandlerFactory = _ => new SharedHandler(handler);
                })
                // Payloads deserialize with the same options as every REST response, so a record's
                // values arrive as .NET values here too rather than as parser artifacts.
                .AddJsonProtocol(json => json.PayloadSerializerOptions = MorphDBJson.Options)
                .WithAutomaticReconnect();

            var connection = builder.Build();

            connection.On<RecordChangedMessage>("RecordCreated",
                message => Dispatch(message.Table, ToNotification(message, ChangeOperation.Insert)));
            connection.On<RecordChangedMessage>("RecordUpdated",
                message => Dispatch(message.Table, ToNotification(message, ChangeOperation.Update)));
            connection.On<RecordDeletedMessage>("RecordDeleted",
                message => Dispatch(message.Table, ToNotification(message)));

            // A reconnect is a new connection to the hub, and the hub's group membership went with
            // the old one — without this, the first dropped connection would silence every
            // subscription for good while IsConnected still said true.
            connection.Reconnected += async _ =>
            {
                foreach (var tableName in SubscribedTables())
                {
                    await connection.InvokeAsync("Subscribe", tableName);
                }
            };

            _connection = connection;
            await connection.StartAsync(cancellationToken);
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

        // The hub subscribes a connection to a table once; how many callbacks share it is this
        // client's business. Only the first subscriber to a table talks to the hub.
        bool first;
        lock (_subscriptionsGate)
        {
            if (!_subscriptions.TryGetValue(tableName, out var subscribers))
            {
                subscribers = [];
                _subscriptions[tableName] = subscribers;
            }

            first = subscribers.Count == 0;
            subscribers.Add(subscription);
        }

        if (first)
        {
            try
            {
                await _connection.InvokeAsync("Subscribe", tableName, cancellationToken);
            }
            catch
            {
                Forget(subscription);
                throw;
            }
        }

        return subscription;
    }

    /// <summary>
    /// Drops one subscriber; the hub hears about it only when the table's last subscriber leaves.
    /// </summary>
    private async Task UnsubscribeAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        var last = Forget(subscription);

        if (last && _connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("Unsubscribe", subscription.TableName, cancellationToken);
        }
    }

    /// <summary>
    /// Removes the subscriber locally. True when it was the table's last one.
    /// </summary>
    private bool Forget(Subscription subscription)
    {
        lock (_subscriptionsGate)
        {
            if (!_subscriptions.TryGetValue(subscription.TableName, out var subscribers))
                return false;

            subscribers.Remove(subscription);
            if (subscribers.Count > 0)
                return false;

            _subscriptions.Remove(subscription.TableName);
            return true;
        }
    }

    private string[] SubscribedTables()
    {
        lock (_subscriptionsGate)
        {
            return [.. _subscriptions.Keys];
        }
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

        lock (_subscriptionsGate)
        {
            _subscriptions.Clear();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectionLock.Dispose();
    }

    private void Dispatch(string tableName, ChangeNotification notification)
    {
        Subscription[] subscribers;
        lock (_subscriptionsGate)
        {
            if (!_subscriptions.TryGetValue(tableName, out var list))
                return;

            subscribers = [.. list];
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.OnChange?.Invoke(notification);
        }
    }

    private ChangeNotification ToNotification(RecordChangedMessage message, ChangeOperation operation) => new()
    {
        TableName = message.Table,
        Operation = operation,
        RecordId = message.RecordId,
        Data = message.Data,
        ProjectId = _options.ProjectId,
        Timestamp = message.Timestamp,
    };

    private ChangeNotification ToNotification(RecordDeletedMessage message) => new()
    {
        TableName = message.Table,
        Operation = ChangeOperation.Delete,
        RecordId = message.RecordId,
        Data = null,
        ProjectId = _options.ProjectId,
        Timestamp = message.Timestamp,
    };

    /// <summary>
    /// The <c>RecordCreated</c> / <c>RecordUpdated</c> payload, as the hub sends it.
    /// </summary>
    private sealed class RecordChangedMessage
    {
        public required string Table { get; init; }
        public Guid? RecordId { get; init; }
        public required string Operation { get; init; }
        public IDictionary<string, object?>? Data { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// The <c>RecordDeleted</c> payload, as the hub sends it — no <c>Data</c>, because the row is gone.
    /// </summary>
    private sealed class RecordDeletedMessage
    {
        public required string Table { get; init; }
        public Guid? RecordId { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Lends a handler to the hub connection without handing over its lifetime — the inner handler
    /// belongs to the <see cref="MorphDBClient"/>, so disposing this one leaves it alone.
    /// </summary>
    private sealed class SharedHandler : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _inner;

        public SharedHandler(HttpMessageHandler inner)
        {
            _inner = new HttpMessageInvoker(inner, disposeHandler: false);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _inner.SendAsync(request, cancellationToken);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _inner.Send(request, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }
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
                _isActive = false;
                await _client.UnsubscribeAsync(this, cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await UnsubscribeAsync();
        }
    }
}
