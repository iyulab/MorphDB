using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
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

        var groupName = MorphHub.GetTableGroupName(changeEvent.TenantId, changeEvent.Table);

        switch (changeEvent.Operation.ToUpperInvariant())
        {
            case "INSERT":
                await BroadcastRecordCreatedAsync(groupName, changeEvent);
                break;

            case "UPDATE":
                await BroadcastRecordUpdatedAsync(groupName, changeEvent);
                break;

            case "DELETE":
                await BroadcastRecordDeletedAsync(groupName, changeEvent);
                break;
        }
    }

    private async Task BroadcastRecordCreatedAsync(string groupName, DatabaseChangeEvent changeEvent)
    {
        var message = new RecordChangedMessage
        {
            Table = changeEvent.Table,
            RecordId = changeEvent.RecordId,
            Operation = "INSERT",
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
            Operation = "UPDATE",
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
    private static partial void LogChangeReceived(ILogger logger, string operation, string table, Guid recordId);
}

/// <summary>
/// Event structure from PostgreSQL NOTIFY payload.
/// </summary>
internal sealed class DatabaseChangeEvent
{
    public Guid TenantId { get; init; }
    public required string Table { get; init; }
    public required string Operation { get; init; }
    public Guid RecordId { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
