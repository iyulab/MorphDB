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
    private readonly ISchemaManager _schemaManager;
    private readonly string _connectionString;

    public PostgresChangeListener(
        ILogger<PostgresChangeListener> logger,
        IServiceScopeFactory scopeFactory,
        IHubContext<MorphHub, IMorphHubClient> hubContext,
        SubscriptionManager subscriptionManager,
        ISchemaManager schemaManager,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _subscriptionManager = subscriptionManager;
        _schemaManager = schemaManager;
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

        // Translated once here so every consumer downstream (the SignalR broadcast and the
        // webhook filter/payload) sees the same logical-only vocabulary every other surface
        // uses -- see TranslateToLogicalAsync.
        var logicalData = await TranslateToLogicalAsync(changeEvent);

        var groupName = MorphHub.GetTableGroupName(changeEvent.ProjectId, changeEvent.Table);

        switch (changeEvent.Operation.ToUpperInvariant())
        {
            case ChangeOperation.Insert:
                await BroadcastRecordCreatedAsync(groupName, changeEvent, logicalData);
                await DeliverWebhooksAsync(changeEvent, logicalData, WebhookEvent.Insert);
                break;

            case ChangeOperation.Update:
                await BroadcastRecordUpdatedAsync(groupName, changeEvent, logicalData);
                await DeliverWebhooksAsync(changeEvent, logicalData, WebhookEvent.Update);
                break;

            case ChangeOperation.Delete:
                await BroadcastRecordDeletedAsync(groupName, changeEvent);
                await DeliverWebhooksAsync(changeEvent, logicalData, WebhookEvent.Delete);
                break;
        }
    }

    /// <summary>
    /// Translates the trigger's physical row (<c>to_jsonb(NEW)</c> — see
    /// <c>ChangeNotificationSetup.cs</c>) into the logical vocabulary every other surface (REST,
    /// GraphQL, export, view) already speaks, and drops <c>project_id</c> the same way
    /// <c>RowMapper</c> does for those surfaces. Unlike <c>RowMapper</c>, this does not convert
    /// value *types* -- the values are already <see cref="JsonElement"/>s from deserializing the
    /// NOTIFY payload, and go back out as JSON over SignalR/webhooks unchanged, so only the keys
    /// need translating.
    /// </summary>
    private async Task<IDictionary<string, object?>?> TranslateToLogicalAsync(DatabaseChangeEvent changeEvent)
    {
        if (changeEvent.Data is null)
            return null;

        var table = await _schemaManager.GetTableByIdAsync(changeEvent.TableId);
        if (table is null)
        {
            // The table a just-committed write's trigger fired for should always resolve; if it
            // doesn't (dropped between commit and notify), fail closed rather than let physical
            // column names reach a consumer with no way to translate them itself.
            LogTranslationTableMissing(_logger, changeEvent.TableId, changeEvent.Table);
            return null;
        }

        var physicalToLogical = table.Columns.ToDictionary(c => c.PhysicalName, c => c.LogicalName, StringComparer.Ordinal);
        var logical = new Dictionary<string, object?>();
        foreach (var (key, value) in changeEvent.Data)
        {
            if (string.Equals(key, SystemColumns.ProjectId, StringComparison.Ordinal))
            {
                continue;
            }

            // A key absent from the table's declared columns is already logical -- every system
            // column (_id, _created_at, ...) has physical name == logical name, so it never
            // appears in physicalToLogical and passes through here unchanged.
            logical[physicalToLogical.TryGetValue(key, out var logicalName) ? logicalName : key] = value;
        }

        return logical;
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

    private async Task BroadcastRecordCreatedAsync(
        string groupName, DatabaseChangeEvent changeEvent, IDictionary<string, object?>? logicalData)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = ChangeOperation.Insert,
            Data = logicalData ?? new Dictionary<string, object?>(),
            Timestamp = changeEvent.Timestamp
        };

        await _hubContext.Clients.Group(groupName).RecordCreated(message);
    }

    private async Task BroadcastRecordUpdatedAsync(
        string groupName, DatabaseChangeEvent changeEvent, IDictionary<string, object?>? logicalData)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = ChangeOperation.Update,
            Data = logicalData ?? new Dictionary<string, object?>(),
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Change notification for table {TableId} ({Table}) referenced a table that no longer resolves; dropping its row data rather than broadcasting it untranslated")]
    private static partial void LogTranslationTableMissing(ILogger logger, Guid tableId, string table);
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
