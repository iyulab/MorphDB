using System.Text.Json;
using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages webhook subscriptions and deliveries.
/// </summary>
public interface IWebhookManager
{
    /// <summary>
    /// Creates a new webhook subscription.
    /// </summary>
    Task<WebhookMetadata> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a webhook by ID.
    /// </summary>
    Task<WebhookMetadata?> GetWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists webhooks for a tenant.
    /// </summary>
    Task<IReadOnlyList<WebhookMetadata>> ListWebhooksAsync(
        Guid tenantId,
        string? tableName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a webhook subscription.
    /// </summary>
    Task<WebhookMetadata> UpdateWebhookAsync(
        UpdateWebhookRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a webhook subscription.
    /// </summary>
    Task DeleteWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets webhooks subscribed to specific table and event.
    /// </summary>
    Task<IReadOnlyList<WebhookMetadata>> GetSubscribedWebhooksAsync(
        Guid tenantId,
        Guid tableId,
        WebhookEvent webhookEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a delivery attempt.
    /// </summary>
    Task<WebhookDelivery> CreateDeliveryAsync(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates delivery status after attempt.
    /// </summary>
    Task UpdateDeliveryAsync(
        UpdateDeliveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending deliveries for retry.
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> GetPendingDeliveriesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets delivery history for a webhook.
    /// </summary>
    Task<IReadOnlyList<WebhookDelivery>> GetDeliveryHistoryAsync(
        Guid webhookId,
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates the webhook secret.
    /// </summary>
    Task<string> RegenerateSecretAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default);
}

#region Request Models

public sealed record CreateWebhookRequest
{
    public Guid TenantId { get; init; }
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required string Url { get; init; }
    public IReadOnlyList<WebhookEvent> Events { get; init; } = [];
    public JsonDocument? Filter { get; init; }
    public JsonDocument? Headers { get; init; }
}

public sealed record UpdateWebhookRequest
{
    public Guid WebhookId { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<WebhookEvent>? Events { get; init; }
    public JsonDocument? Filter { get; init; }
    public JsonDocument? Headers { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CreateDeliveryRequest
{
    public Guid WebhookId { get; init; }
    public Guid? RecordId { get; init; }
    public required WebhookEvent Event { get; init; }
    public required JsonDocument Payload { get; init; }
}

public sealed record UpdateDeliveryRequest
{
    public Guid DeliveryId { get; init; }
    public required DeliveryStatus Status { get; init; }
    public int AttemptCount { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
}

#endregion
