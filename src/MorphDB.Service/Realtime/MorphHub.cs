using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace MorphDB.Service.Realtime;

/// <summary>
/// SignalR Hub for MorphDB real-time data synchronization.
/// Clients can subscribe to table changes and receive real-time updates.
/// </summary>
public sealed partial class MorphHub : Hub<IMorphHubClient>
{
    private readonly ILogger<MorphHub> _logger;
    private readonly SubscriptionManager _subscriptionManager;

    public MorphHub(ILogger<MorphHub> logger, SubscriptionManager subscriptionManager)
    {
        _logger = logger;
        _subscriptionManager = subscriptionManager;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var projectId = GetProjectId();
        LogClientConnected(_logger, Context.ConnectionId, projectId);

        await Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var projectId = GetProjectId();
        LogClientDisconnected(_logger, Context.ConnectionId, projectId, exception);

        // Remove all subscriptions for this connection
        _subscriptionManager.RemoveConnection(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to changes for a specific table.
    /// </summary>
    /// <param name="tableName">The logical table name to subscribe to.</param>
    /// <param name="options">Optional subscription options for filtering.</param>
    public async Task Subscribe(string tableName, SubscriptionOptions? options = null)
    {
        var projectId = GetProjectId();
        var groupName = GetTableGroupName(projectId, tableName);

        _subscriptionManager.AddSubscription(Context.ConnectionId, projectId, tableName, options);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        LogSubscribed(_logger, Context.ConnectionId, tableName, projectId);

        await Clients.Caller.Subscribed(tableName);
    }

    /// <summary>
    /// Unsubscribe from changes for a specific table.
    /// </summary>
    /// <param name="tableName">The logical table name to unsubscribe from.</param>
    public async Task Unsubscribe(string tableName)
    {
        var projectId = GetProjectId();
        var groupName = GetTableGroupName(projectId, tableName);

        _subscriptionManager.RemoveSubscription(Context.ConnectionId, tableName);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        LogUnsubscribed(_logger, Context.ConnectionId, tableName, projectId);

        await Clients.Caller.Unsubscribed(tableName);
    }

    /// <summary>
    /// Subscribe to changes for multiple tables.
    /// </summary>
    /// <param name="tableNames">The logical table names to subscribe to.</param>
    public async Task SubscribeMany(IEnumerable<string> tableNames)
    {
        foreach (var tableName in tableNames)
        {
            await Subscribe(tableName);
        }
    }

    /// <summary>
    /// Unsubscribe from changes for multiple tables.
    /// </summary>
    /// <param name="tableNames">The logical table names to unsubscribe from.</param>
    public async Task UnsubscribeMany(IEnumerable<string> tableNames)
    {
        foreach (var tableName in tableNames)
        {
            await Unsubscribe(tableName);
        }
    }

    /// <summary>
    /// Get the list of tables this connection is subscribed to.
    /// </summary>
    public Task<IReadOnlyList<string>> GetSubscriptions()
    {
        var subscriptions = _subscriptionManager.GetSubscriptions(Context.ConnectionId);
        return Task.FromResult(subscriptions);
    }

    private Guid GetProjectId()
    {
        var httpContext = Context.GetHttpContext();
        var projectIdHeader = httpContext?.Request.Headers["X-Project-Id"].FirstOrDefault();

        if (Guid.TryParse(projectIdHeader, out var projectId))
        {
            return projectId;
        }

        // Default project for development
        return Guid.Empty;
    }

    internal static string GetTableGroupName(Guid projectId, string tableName)
    {
        return $"table:{projectId}:{tableName}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ConnectionId} connected for project {ProjectId}")]
    private static partial void LogClientConnected(ILogger logger, string connectionId, Guid projectId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ConnectionId} disconnected from project {ProjectId}")]
    private static partial void LogClientDisconnected(ILogger logger, string connectionId, Guid projectId, Exception? exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ConnectionId} subscribed to table {TableName} for project {ProjectId}")]
    private static partial void LogSubscribed(ILogger logger, string connectionId, string tableName, Guid projectId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ConnectionId} unsubscribed from table {TableName} for project {ProjectId}")]
    private static partial void LogUnsubscribed(ILogger logger, string connectionId, string tableName, Guid projectId);
}

/// <summary>
/// Client interface for MorphHub.
/// </summary>
public interface IMorphHubClient
{
    /// <summary>
    /// Called when a record is created.
    /// </summary>
    Task RecordCreated(RecordChangedMessage message);

    /// <summary>
    /// Called when a record is updated.
    /// </summary>
    Task RecordUpdated(RecordChangedMessage message);

    /// <summary>
    /// Called when a record is deleted.
    /// </summary>
    Task RecordDeleted(RecordDeletedMessage message);

    /// <summary>
    /// Called when successfully subscribed to a table.
    /// </summary>
    Task Subscribed(string tableName);

    /// <summary>
    /// Called when successfully unsubscribed from a table.
    /// </summary>
    Task Unsubscribed(string tableName);

    /// <summary>
    /// Called when an error occurs.
    /// </summary>
    Task OnError(ErrorMessage message);
}

/// <summary>
/// Message sent when a record is created or updated.
/// </summary>
public sealed class RecordChangedMessage
{
    public required string Table { get; init; }
    public Guid? RecordId { get; init; }
    public required string Operation { get; init; }
    public required IDictionary<string, object?> Data { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Message sent when a record is deleted.
/// </summary>
public sealed class RecordDeletedMessage
{
    public required string Table { get; init; }
    public Guid? RecordId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Error message sent to clients.
/// </summary>
public sealed class ErrorMessage
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Options for table subscriptions.
/// </summary>
public sealed class SubscriptionOptions
{
    /// <summary>
    /// Filter expression for the subscription (e.g., "status:eq:active").
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Fields to include in change notifications. If null, all fields are included.
    /// </summary>
    public IReadOnlyList<string>? Fields { get; init; }

    /// <summary>
    /// Whether to include the full record data in notifications.
    /// </summary>
    public bool IncludeData { get; init; } = true;
}

/// <summary>
/// Manages subscription state for connected clients.
/// </summary>
public sealed class SubscriptionManager
{
    private readonly ConcurrentDictionary<string, ConnectionSubscriptions> _connections = new();

    public void AddSubscription(string connectionId, Guid projectId, string tableName, SubscriptionOptions? options)
    {
        var subscriptions = _connections.GetOrAdd(connectionId, _ => new ConnectionSubscriptions(projectId));
        subscriptions.Tables[tableName] = options ?? new SubscriptionOptions();
    }

    public void RemoveSubscription(string connectionId, string tableName)
    {
        if (_connections.TryGetValue(connectionId, out var subscriptions))
        {
            subscriptions.Tables.TryRemove(tableName, out _);
        }
    }

    public void RemoveConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public IReadOnlyList<string> GetSubscriptions(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var subscriptions))
        {
            return subscriptions.Tables.Keys.ToList();
        }
        return [];
    }

    public SubscriptionOptions? GetSubscriptionOptions(string connectionId, string tableName)
    {
        if (_connections.TryGetValue(connectionId, out var subscriptions) &&
            subscriptions.Tables.TryGetValue(tableName, out var options))
        {
            return options;
        }
        return null;
    }

    public IEnumerable<(string ConnectionId, SubscriptionOptions Options)> GetSubscribersForTable(Guid projectId, string tableName)
    {
        foreach (var (connectionId, subscriptions) in _connections)
        {
            if (subscriptions.ProjectId == projectId &&
                subscriptions.Tables.TryGetValue(tableName, out var options))
            {
                yield return (connectionId, options);
            }
        }
    }

    private sealed class ConnectionSubscriptions
    {
        public Guid ProjectId { get; }
        public ConcurrentDictionary<string, SubscriptionOptions> Tables { get; } = new();

        public ConnectionSubscriptions(Guid projectId)
        {
            ProjectId = projectId;
        }
    }
}
