namespace MorphDB.Client.Models;

/// <summary>
/// Request to create a webhook subscription.
/// </summary>
public sealed class CreateWebhookRequest
{
    /// <summary>
    /// Webhook name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Table name to subscribe to.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Webhook delivery URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Events to subscribe to (insert, update, delete).
    /// </summary>
    public IReadOnlyList<string> Events { get; init; } = ["insert", "update", "delete"];

    /// <summary>
    /// Optional filter expression.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Custom headers to include in webhook requests.
    /// </summary>
    public IDictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Webhook subscription information.
/// </summary>
public sealed class WebhookInfo
{
    /// <summary>
    /// Webhook ID.
    /// </summary>
    public Guid WebhookId { get; init; }

    /// <summary>
    /// Webhook name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Delivery URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Subscribed events.
    /// </summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>
    /// Whether the webhook is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Webhook delivery record.
/// </summary>
public sealed class WebhookDelivery
{
    /// <summary>
    /// Delivery ID.
    /// </summary>
    public Guid DeliveryId { get; init; }

    /// <summary>
    /// Webhook ID.
    /// </summary>
    public Guid WebhookId { get; init; }

    /// <summary>
    /// Associated record ID.
    /// </summary>
    public Guid? RecordId { get; init; }

    /// <summary>
    /// Event type.
    /// </summary>
    public required string Event { get; init; }

    /// <summary>
    /// Delivery status.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Number of delivery attempts.
    /// </summary>
    public int AttemptCount { get; init; }

    /// <summary>
    /// HTTP status code from last attempt.
    /// </summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Delivery timestamp.
    /// </summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}
