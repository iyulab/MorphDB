using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for delivering webhook payloads to subscribers.
/// </summary>
public interface IWebhookDeliveryService
{
    /// <summary>
    /// Queues a webhook delivery for processing.
    /// </summary>
    Task QueueDeliveryAsync(
        WebhookMetadata webhook,
        WebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers a webhook payload immediately.
    /// </summary>
    Task<DeliveryResult> DeliverAsync(
        WebhookMetadata webhook,
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a webhook delivery attempt.
/// </summary>
public sealed record DeliveryResult
{
    public bool Success { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
