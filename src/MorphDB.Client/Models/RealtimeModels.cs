namespace MorphDB.Client.Models;

/// <summary>
/// Real-time change notification.
/// </summary>
public sealed class ChangeNotification
{
    /// <summary>
    /// Table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Change operation type.
    /// </summary>
    public required ChangeOperation Operation { get; init; }

    /// <summary>
    /// Record ID.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Record data (for insert and update).
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Previous data (for update operations).
    /// </summary>
    public IDictionary<string, object?>? OldData { get; init; }

    /// <summary>
    /// Project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Timestamp of the change.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Change operation types.
/// </summary>
public enum ChangeOperation
{
    /// <summary>Record inserted.</summary>
    Insert,

    /// <summary>Record updated.</summary>
    Update,

    /// <summary>Record deleted.</summary>
    Delete
}

/// <summary>
/// Subscription handle for managing subscriptions.
/// </summary>
public interface ISubscription : IAsyncDisposable
{
    /// <summary>
    /// Subscription ID.
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    /// Table name.
    /// </summary>
    string TableName { get; }

    /// <summary>
    /// Whether the subscription is active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Unsubscribes from the table.
    /// </summary>
    Task UnsubscribeAsync(CancellationToken cancellationToken = default);
}
