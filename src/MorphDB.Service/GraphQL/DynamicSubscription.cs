using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Dynamic GraphQL subscription type for MorphDB real-time updates.
/// </summary>
public sealed class DynamicSubscription
{
    /// <summary>
    /// Subscribes to record creation events for a specific table.
    /// </summary>
    [GraphQLDescription("Subscribes to record creation events for a specific table")]
    [Subscribe]
    [Topic("{table}_created")]
    public RecordChangeEvent OnRecordCreated(
        string table,
        [EventMessage] RecordChangeEvent changeEvent) => changeEvent;

    /// <summary>
    /// Subscribes to record update events for a specific table.
    /// </summary>
    [GraphQLDescription("Subscribes to record update events for a specific table")]
    [Subscribe]
    [Topic("{table}_updated")]
    public RecordChangeEvent OnRecordUpdated(
        string table,
        [EventMessage] RecordChangeEvent changeEvent) => changeEvent;

    /// <summary>
    /// Subscribes to record deletion events for a specific table.
    /// </summary>
    [GraphQLDescription("Subscribes to record deletion events for a specific table")]
    [Subscribe]
    [Topic("{table}_deleted")]
    public RecordDeleteEvent OnRecordDeleted(
        string table,
        [EventMessage] RecordDeleteEvent deleteEvent) => deleteEvent;

    /// <summary>
    /// Subscribes to all record changes for a specific table.
    /// </summary>
    [GraphQLDescription("Subscribes to all record changes for a specific table")]
    [Subscribe]
    [Topic("{table}_changed")]
    public RecordChangeEvent OnRecordChanged(
        string table,
        [EventMessage] RecordChangeEvent changeEvent) => changeEvent;
}

/// <summary>
/// Event for record creation and update.
/// </summary>
public sealed class RecordChangeEvent
{
    public required string Table { get; init; }
    public required ChangeType ChangeType { get; init; }
    public required RecordNode Record { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event for record deletion.
/// </summary>
public sealed class RecordDeleteEvent
{
    public required string Table { get; init; }
    public Guid RecordId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Type of change for subscription events.
/// </summary>
public enum ChangeType
{
    Created,
    Updated,
    Deleted
}

/// <summary>
/// Service for publishing subscription events.
/// </summary>
public interface ISubscriptionEventSender
{
    /// <summary>
    /// Sends a record created event.
    /// </summary>
    Task SendRecordCreatedAsync(string table, RecordNode record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a record updated event.
    /// </summary>
    Task SendRecordUpdatedAsync(string table, RecordNode record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a record deleted event.
    /// </summary>
    Task SendRecordDeletedAsync(string table, Guid recordId, CancellationToken cancellationToken = default);
}

/// <summary>
/// HotChocolate-based subscription event sender.
/// Uses lazy resolution to avoid DI issues during startup.
/// </summary>
public sealed class HotChocolateSubscriptionEventSender : ISubscriptionEventSender
{
    private readonly IServiceProvider _serviceProvider;

    public HotChocolateSubscriptionEventSender(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private ITopicEventSender? GetEventSender()
    {
        return _serviceProvider.GetService<ITopicEventSender>();
    }

    public async Task SendRecordCreatedAsync(string table, RecordNode record, CancellationToken cancellationToken = default)
    {
        var eventSender = GetEventSender();
        if (eventSender is null)
            return; // Subscriptions not configured

        var changeEvent = new RecordChangeEvent
        {
            Table = table,
            ChangeType = ChangeType.Created,
            Record = record
        };

        await eventSender.SendAsync($"{table}_created", changeEvent, cancellationToken);
        await eventSender.SendAsync($"{table}_changed", changeEvent, cancellationToken);
    }

    public async Task SendRecordUpdatedAsync(string table, RecordNode record, CancellationToken cancellationToken = default)
    {
        var eventSender = GetEventSender();
        if (eventSender is null)
            return; // Subscriptions not configured

        var changeEvent = new RecordChangeEvent
        {
            Table = table,
            ChangeType = ChangeType.Updated,
            Record = record
        };

        await eventSender.SendAsync($"{table}_updated", changeEvent, cancellationToken);
        await eventSender.SendAsync($"{table}_changed", changeEvent, cancellationToken);
    }

    public async Task SendRecordDeletedAsync(string table, Guid recordId, CancellationToken cancellationToken = default)
    {
        var eventSender = GetEventSender();
        if (eventSender is null)
            return; // Subscriptions not configured

        var deleteEvent = new RecordDeleteEvent
        {
            Table = table,
            RecordId = recordId
        };

        var changeEvent = new RecordChangeEvent
        {
            Table = table,
            ChangeType = ChangeType.Deleted,
            Record = new RecordNode { Id = recordId }
        };

        await eventSender.SendAsync($"{table}_deleted", deleteEvent, cancellationToken);
        await eventSender.SendAsync($"{table}_changed", changeEvent, cancellationToken);
    }
}
