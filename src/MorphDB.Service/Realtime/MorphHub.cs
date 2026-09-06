using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using MorphDB.Core.Exceptions;

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
        // Disconnect must not fail on a connection that never named a project — the connect attempt
        // already refused that one, and there is nothing left to refuse.
        var projectId = ProjectIdOrNull() ?? Guid.Empty;
        LogClientDisconnected(_logger, Context.ConnectionId, projectId, exception);

        // Remove all subscriptions for this connection
        _subscriptionManager.RemoveConnection(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to changes for a specific table.
    /// </summary>
    /// <param name="tableName">The logical table name to subscribe to.</param>
    /// <remarks>
    /// One parameter, and a C# default would not have made it optional. SignalR binds an
    /// invocation by argument *count* — a caller sending one argument to a two-parameter method is
    /// refused at the binder with <c>Invocation provides 1 argument(s) but target expects 2</c>,
    /// whatever default the declaration carries, because no wire protocol conveys a C# default. A
    /// second <c>options</c> parameter used to sit here for a filter, a field list and an
    /// include-data flag that nothing ever read, which meant the only reachable effect of the
    /// argument was to decide whether the documented one-argument call worked.
    /// </remarks>
    public async Task Subscribe(string tableName)
    {
        var projectId = GetProjectId();
        var groupName = GetTableGroupName(projectId, tableName);

        _subscriptionManager.AddSubscription(Context.ConnectionId, projectId, tableName);
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

    /// <summary>
    /// The project this connection is scoped to.
    /// <para>
    /// A connection that does not name one used to fall back to <see cref="Guid.Empty"/> "for
    /// development". Nothing publishes to that project, so the client would subscribe successfully
    /// and then receive nothing, forever, with no error to explain it. Failing here says so.
    /// </para>
    /// </summary>
    private Guid GetProjectId() => ProjectIdOrNull() ?? throw new MissingProjectException();

    private Guid? ProjectIdOrNull()
    {
        var projectIdHeader = Context.GetHttpContext()?.Request.Headers["X-Project-Id"].FirstOrDefault();

        return Guid.TryParse(projectIdHeader, out var projectId) && projectId != Guid.Empty
            ? projectId
            : null;
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
/// Manages subscription state for connected clients.
/// </summary>
/// <remarks>
/// A subscription is a table name and nothing else. It used to also carry a
/// <c>SubscriptionOptions</c> — a filter, a field list, an include-data flag — which every
/// subscribe call stored and no broadcast ever read; the two accessors that could have read it
/// had no callers at all. Keeping it would have meant either documenting an option that does
/// nothing or leaving the documentation to say so, which it did.
/// </remarks>
public sealed class SubscriptionManager
{
    private readonly ConcurrentDictionary<string, ConnectionSubscriptions> _connections = new();

    public void AddSubscription(string connectionId, Guid projectId, string tableName)
    {
        var subscriptions = _connections.GetOrAdd(connectionId, _ => new ConnectionSubscriptions(projectId));
        subscriptions.Tables[tableName] = 0;
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

    private sealed class ConnectionSubscriptions
    {
        public Guid ProjectId { get; }

        /// <summary>The tables this connection is subscribed to; the value carries nothing.</summary>
        public ConcurrentDictionary<string, byte> Tables { get; } = new();

        public ConnectionSubscriptions(Guid projectId)
        {
            ProjectId = projectId;
        }
    }
}
