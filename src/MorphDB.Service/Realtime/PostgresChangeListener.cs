using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Listens for PostgreSQL NOTIFY events and broadcasts changes to connected clients.
/// <para>
/// Notifications are handled one at a time, in the order they arrive. PostgreSQL delivers them in
/// the order the transactions committed, and that order is the only thing a subscriber has to tell
/// two changes to one row apart — so the LISTEN connection's event handler does nothing but hand
/// each payload to a bounded channel, and a single consumer loop drains it. Handling a payload
/// inside the event handler itself (an <c>async void</c>, since Npgsql's handler is a plain
/// <c>void</c> delegate) let handlers overlap on their awaits and broadcast out of commit order.
/// The channel is bounded so a burst of writes cannot start an unbounded number of handlers: when
/// it fills, the LISTEN connection simply stops asking for the next notification until there is
/// room, and PostgreSQL queues the rest on its side.
/// </para>
/// </summary>
public sealed partial class PostgresChangeListener : BackgroundService
{
    private const string ChannelName = "morphdb_changes";

    /// <summary>
    /// Notifications waiting to be handled. Sized so a bulk write does not stall the LISTEN
    /// connection on every row, and small enough that a slow downstream (webhook queueing, a
    /// schema lookup) is felt as backpressure rather than as memory.
    /// </summary>
    private const int PendingNotificationCapacity = 1024;

    /// <summary>
    /// Seconds of silence after which Npgsql sends a keepalive on the LISTEN connection. A
    /// connection that only ever waits is exactly the one a NAT or load balancer drops for idling,
    /// and without this the drop is only noticed at the next notification — which is then lost
    /// along with every one committed until the reconnect.
    /// </summary>
    private const int KeepAliveSeconds = 30;

    private readonly ILogger<PostgresChangeListener> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<MorphHub, IMorphHubClient> _hubContext;
    private readonly IMorphDataService _dataService;
    private readonly string _connectionString;
    private readonly Channel<string> _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(PendingNotificationCapacity)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    public PostgresChangeListener(
        ILogger<PostgresChangeListener> logger,
        IServiceScopeFactory scopeFactory,
        IHubContext<MorphHub, IMorphHubClient> hubContext,
        IMorphDataService dataService,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _dataService = dataService;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogListenerStarting(_logger);

        var consumer = ConsumePendingAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ListenForChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    LogListenerError(_logger, ex);

                    // Wait before reconnecting
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        finally
        {
            _pending.Writer.TryComplete();
            await consumer;
        }

        LogListenerStopped(_logger);
    }

    private async Task ListenForChangesAsync(CancellationToken stoppingToken)
    {
        var connectionSettings = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            KeepAlive = KeepAliveSeconds,
        };

        await using var dataSource = NpgsqlDataSource.Create(connectionSettings);
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);

        // Npgsql raises this synchronously, from inside WaitAsync, once per notification and in
        // delivery order. It only collects; the payloads are queued after WaitAsync returns, so the
        // wait on a full channel happens on this loop and not inside the driver's event dispatch.
        var received = new Queue<string>();
        connection.Notification += (_, e) => received.Enqueue(e.Payload);

        await using (var cmd = new NpgsqlCommand($"LISTEN {ChannelName}", connection))
        {
            await cmd.ExecuteNonQueryAsync(stoppingToken);
        }

        LogListenerStarted(_logger, ChannelName);

        // Keep the connection open and wait for notifications
        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);

            while (received.TryDequeue(out var payload))
            {
                await _pending.Writer.WriteAsync(payload, stoppingToken);
            }
        }
    }

    /// <summary>
    /// The one place notifications are handled — strictly in the order they were queued.
    /// </summary>
    private async Task ConsumePendingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var payload in _pending.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await HandleNotificationAsync(payload, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown, mid-notification — not an error to report
                    break;
                }
                catch (Exception ex)
                {
                    LogNotificationError(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    private async Task HandleNotificationAsync(string payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(payload))
            return;

        var changeEvent = JsonSerializer.Deserialize<DatabaseChangeEvent>(payload, JsonOptions);
        if (changeEvent == null)
            return;

        LogChangeReceived(_logger, changeEvent.Operation, changeEvent.Table, changeEvent.RecordId);

        var groupName = MorphHub.GetTableGroupName(changeEvent.ProjectId, changeEvent.Table);

        switch (changeEvent.Operation.ToUpperInvariant())
        {
            case ChangeOperation.Insert:
                {
                    var data = await ReadRowAsync(changeEvent, cancellationToken);
                    await BroadcastRecordChangedAsync(groupName, changeEvent, ChangeOperation.Insert, data);
                    await DeliverWebhooksAsync(changeEvent, data, WebhookEvent.Insert);
                    break;
                }

            case ChangeOperation.Update:
                {
                    var data = await ReadRowAsync(changeEvent, cancellationToken);
                    await BroadcastRecordChangedAsync(groupName, changeEvent, ChangeOperation.Update, data);
                    await DeliverWebhooksAsync(changeEvent, data, WebhookEvent.Update);
                    break;
                }

            case ChangeOperation.Delete:
                await BroadcastRecordDeletedAsync(groupName, changeEvent);
                await DeliverWebhooksAsync(changeEvent, null, WebhookEvent.Delete);
                break;
        }
    }

    /// <summary>
    /// The row a notification names, read back through the same door REST serves it through — so
    /// the broadcast and the webhook payload carry logical column names, decrypted values and the
    /// system columns exactly as a <c>GET /api/data/{table}/{id}</c> would, with no second
    /// translation to keep in step. The trigger deliberately sends only the key (see
    /// <c>ChangeNotificationSetup</c>).
    /// <para>
    /// This is the row as it stands when it is read, not the image the notifying statement wrote:
    /// a row changed again before the service got to its first notification arrives carrying the
    /// later state on both, and a row already deleted arrives with no data at all. Both are the
    /// documented contract of the real-time surface (<c>docs/API.md</c>).
    /// </para>
    /// </summary>
    private async Task<IDictionary<string, object?>> ReadRowAsync(DatabaseChangeEvent changeEvent, CancellationToken cancellationToken)
    {
        if (changeEvent.RecordId is not { } recordId)
        {
            return new Dictionary<string, object?>();
        }

        var row = await _dataService.GetByIdAsync(changeEvent.ProjectId, changeEvent.Table, recordId, cancellationToken);
        if (row is null)
        {
            LogRowGoneBeforeRead(_logger, changeEvent.Operation, changeEvent.Table, recordId);
            return new Dictionary<string, object?>();
        }

        return row;
    }

    /// <summary>
    /// The same change event SignalR just broadcast, offered to a second consumer: any webhook
    /// subscribed to this table and event, narrowed by <see cref="WebhookFilterMatcher"/> to the
    /// rows its <c>Filter</c> admits.
    /// </summary>
    private async Task DeliverWebhooksAsync(
        DatabaseChangeEvent changeEvent, IDictionary<string, object?>? logicalData, WebhookEvent webhookEvent)
    {
        using var scope = _scopeFactory.CreateScope();
        var webhookManager = scope.ServiceProvider.GetRequiredService<IWebhookManager>();

        var webhooks = await webhookManager.GetSubscribedWebhooksAsync(
            changeEvent.ProjectId, changeEvent.TableId, webhookEvent);

        if (webhooks.Count == 0)
        {
            return;
        }

        var payload = new WebhookPayload
        {
            Event = webhookEvent.ToString().ToLowerInvariant(),
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Timestamp = changeEvent.Timestamp,
            Data = logicalData,
        };

        var deliveryService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();
        foreach (var webhook in webhooks)
        {
            if (!WebhookFilterMatcher.Matches(webhook.Filter, logicalData))
            {
                continue;
            }

            try
            {
                await deliveryService.QueueDeliveryAsync(webhook, payload);
            }
            catch (Exception ex)
            {
                LogWebhookQueueError(_logger, webhook.WebhookId, ex);
            }
        }
    }

    private async Task BroadcastRecordChangedAsync(
        string groupName, DatabaseChangeEvent changeEvent, string operation, IDictionary<string, object?> data)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = operation,
            Data = data,
            Timestamp = changeEvent.Timestamp
        };

        if (operation == ChangeOperation.Insert)
        {
            await _hubContext.Clients.Group(groupName).RecordCreated(message);
        }
        else
        {
            await _hubContext.Clients.Group(groupName).RecordUpdated(message);
        }
    }

    private async Task BroadcastRecordDeletedAsync(string groupName, DatabaseChangeEvent changeEvent)
    {
        var message = new RecordDeletedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Timestamp = changeEvent.Timestamp
        };

        await _hubContext.Clients.Group(groupName).RecordDeleted(message);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener starting")]
    private static partial void LogListenerStarting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener started, listening on channel {ChannelName}")]
    private static partial void LogListenerStarted(ILogger logger, string channelName);

    [LoggerMessage(Level = LogLevel.Information, Message = "PostgreSQL change listener stopped")]
    private static partial void LogListenerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "PostgreSQL change listener error, will reconnect")]
    private static partial void LogListenerError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error handling notification")]
    private static partial void LogNotificationError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Change received: {Operation} on {Table}, record {RecordId}")]
    private static partial void LogChangeReceived(ILogger logger, string operation, string table, Guid? recordId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to queue webhook delivery for webhook {WebhookId}")]
    private static partial void LogWebhookQueueError(ILogger logger, Guid webhookId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Operation} on {Table}, record {RecordId}: the row was gone before it could be read back; broadcasting the change without data")]
    private static partial void LogRowGoneBeforeRead(ILogger logger, string operation, string table, Guid recordId);
}

/// <summary>
/// The operation values a change event carries on the wire.
/// <para>
/// They are the names PostgreSQL gives the triggering statement, kept upper case, and a client
/// branches on them — so they are a published contract rather than an internal detail. Having one
/// home instead of six literals is what keeps the vocabulary from drifting between the switch that
/// reads it and the messages that send it, and gives the documentation something to be checked
/// against.
/// </para>
/// </summary>
internal static class ChangeOperation
{
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";

    public static readonly IReadOnlyList<string> All = [Insert, Update, Delete];
}

/// <summary>
/// Event structure from PostgreSQL NOTIFY payload — the key of the changed row and nothing else
/// (see <c>ChangeNotificationSetup</c> for why the row itself does not ride along).
/// </summary>
internal sealed class DatabaseChangeEvent
{
    public Guid ProjectId { get; init; }
    public Guid TableId { get; init; }
    public required string Table { get; init; }
    public required string Operation { get; init; }
    public Guid? RecordId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
