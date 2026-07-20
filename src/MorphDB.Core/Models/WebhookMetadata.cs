using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents a webhook subscription for a table.
/// </summary>
public sealed class WebhookMetadata
{
    public Guid WebhookId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required string Url { get; init; }

    /// <summary>
    /// Secret key for HMAC-SHA256 signature generation.
    /// </summary>
    public required string Secret { get; init; }

    /// <summary>
    /// Events to subscribe to: insert, update, delete.
    /// </summary>
    public IReadOnlyList<WebhookEvent> Events { get; init; } = [];

    /// <summary>
    /// Optional filter conditions (JSON format).
    /// </summary>
    public JsonDocument? Filter { get; init; }

    /// <summary>
    /// Custom headers to include in webhook requests.
    /// </summary>
    public JsonDocument? Headers { get; init; }

    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Logical name of the associated table.
    /// </summary>
    public string? TableLogicalName { get; init; }
}

/// <summary>
/// Webhook event types.
/// </summary>
public enum WebhookEvent
{
    Insert,
    Update,
    Delete
}

/// <summary>
/// Represents a webhook delivery attempt.
/// </summary>
public sealed class WebhookDelivery
{
    public Guid DeliveryId { get; init; }
    public Guid WebhookId { get; init; }
    public Guid? RecordId { get; init; }
    public required WebhookEvent Event { get; init; }

    /// <summary>
    /// The payload sent to the webhook URL.
    /// </summary>
    public required JsonDocument Payload { get; init; }

    public DeliveryStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Next retry time (null if no more retries).
    /// </summary>
    public DateTimeOffset? NextRetryAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
}

/// <summary>
/// Webhook delivery status.
/// </summary>
public enum DeliveryStatus
{
    Pending,
    Success,
    Failed,
    Retrying,
    DeadLettered
}

/// <summary>
/// Reason why a delivery was moved to the Dead Letter Queue.
/// </summary>
public enum DlqReason
{
    MaxRetriesExceeded,
    WebhookDeleted,
    WebhookInactive,
    PersistentClientError,
    InvalidPayload,
    Manual
}

/// <summary>
/// Status of a Dead Letter Queue message.
/// </summary>
public enum DlqStatus
{
    PendingReview,
    Resolved,
    Archived,
    Replayed
}

/// <summary>
/// Represents a message in the Dead Letter Queue.
/// </summary>
public sealed class WebhookDlqMessage
{
    public Guid DlqId { get; init; }
    public Guid DeliveryId { get; init; }
    public Guid WebhookId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? RecordId { get; init; }
    public required WebhookEvent Event { get; init; }
    public required JsonDocument Payload { get; init; }

    /// <summary>
    /// The reason this delivery was moved to DLQ.
    /// </summary>
    public required DlqReason Reason { get; init; }

    /// <summary>
    /// Total retry attempts before moving to DLQ.
    /// </summary>
    public int AttemptCount { get; init; }

    /// <summary>
    /// Last HTTP status code received (if any).
    /// </summary>
    public int? LastHttpStatusCode { get; init; }

    /// <summary>
    /// Last error message from delivery attempt.
    /// </summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>
    /// Current DLQ message status.
    /// </summary>
    public DlqStatus Status { get; init; }

    /// <summary>
    /// Notes added during resolution.
    /// </summary>
    public string? ResolutionNotes { get; init; }

    public DateTimeOffset DlqAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
    public Guid? ResolvedBy { get; init; }
}

/// <summary>
/// Webhook payload structure sent to subscribers.
/// </summary>
public sealed class WebhookPayload
{
    public required string Event { get; init; }
    public required string Table { get; init; }
    public Guid? RecordId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public object? Data { get; init; }
    public object? Previous { get; init; }
}
