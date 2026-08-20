using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using Npgsql;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Listens for PostgreSQL NOTIFY events and broadcasts changes to connected clients.
/// </summary>
public sealed partial class PostgresChangeListener : BackgroundService
{
    private const string ChannelName = "morphdb_changes";

    private readonly ILogger<PostgresChangeListener> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<MorphHub, IMorphHubClient> _hubContext;
    private readonly SubscriptionManager _subscriptionManager;
    private readonly string _connectionString;

    public PostgresChangeListener(
        ILogger<PostgresChangeListener> logger,
        IServiceScopeFactory scopeFactory,
        IHubContext<MorphHub, IMorphHubClient> hubContext,
        SubscriptionManager subscriptionManager,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _subscriptionManager = subscriptionManager;
        _connectionString = configuration.GetConnectionString("MorphDB")
            ?? throw new InvalidOperationException("Connection string 'MorphDB' not found.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogListenerStarting(_logger);

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

        LogListenerStopped(_logger);
    }

    private async Task ListenForChangesAsync(CancellationToken stoppingToken)
    {
        await using var dataSource = NpgsqlDataSource.Create(_connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);

        connection.Notification += async (_, e) =>
        {
            try
            {
                await HandleNotificationAsync(e.Payload);
            }
            catch (Exception ex)
            {
                LogNotificationError(_logger, ex);
            }
        };

        await using (var cmd = new NpgsqlCommand($"LISTEN {ChannelName}", connection))
        {
            await cmd.ExecuteNonQueryAsync(stoppingToken);
        }

        LogListenerStarted(_logger, ChannelName);

        // Keep the connection open and wait for notifications
        while (!stoppingToken.IsCancellationRequested)
        {
            await connection.WaitAsync(stoppingToken);
        }
    }

    private async Task HandleNotificationAsync(string payload)
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
                await BroadcastRecordCreatedAsync(groupName, changeEvent);
                await DeliverWebhooksAsync(changeEvent, WebhookEvent.Insert);
                break;

            case ChangeOperation.Update:
                await BroadcastRecordUpdatedAsync(groupName, changeEvent);
                await DeliverWebhooksAsync(changeEvent, WebhookEvent.Update);
                break;

            case ChangeOperation.Delete:
                await BroadcastRecordDeletedAsync(groupName, changeEvent);
                await DeliverWebhooksAsync(changeEvent, WebhookEvent.Delete);
                break;
        }
    }

    /// <summary>
    /// The same change event SignalR just broadcast, offered to a second consumer: any webhook
    /// subscribed to this table and event, narrowed by <see cref="WebhookFilterMatcher"/> to the
    /// rows its <c>Filter</c> admits.
    /// </summary>
    private async Task DeliverWebhooksAsync(DatabaseChangeEvent changeEvent, WebhookEvent webhookEvent)
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
            Data = changeEvent.Data,
        };

        var deliveryService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();
        foreach (var webhook in webhooks)
        {
            if (!WebhookFilterMatcher.Matches(webhook.Filter, changeEvent.Data))
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

    private async Task BroadcastRecordCreatedAsync(string groupName, DatabaseChangeEvent changeEvent)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = ChangeOperation.Insert,
            Data = changeEvent.Data ?? new Dictionary<string, object?>(),
            Timestamp = changeEvent.Timestamp
        };

        await _hubContext.Clients.Group(groupName).RecordCreated(message);
    }

    private async Task BroadcastRecordUpdatedAsync(string groupName, DatabaseChangeEvent changeEvent)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = ChangeOperation.Update,
            Data = changeEvent.Data ?? new Dictionary<string, object?>(),
            Timestamp = changeEvent.Timestamp
        };

        await _hubContext.Clients.Group(groupName).RecordUpdated(message);
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
/// Event structure from PostgreSQL NOTIFY payload.
/// </summary>
internal sealed class DatabaseChangeEvent
{
    public Guid ProjectId { get; init; }
    public Guid TableId { get; init; }
    public required string Table { get; init; }
    public required string Operation { get; init; }
    public Guid? RecordId { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
