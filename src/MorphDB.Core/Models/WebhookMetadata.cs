using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents a webhook subscription for a table.
/// </summary>
public sealed class WebhookMetadata
{
    public Guid WebhookId { get; init; }
    public Guid TenantId { get; init; }
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
    Retrying
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
