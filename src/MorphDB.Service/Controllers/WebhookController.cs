using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

internal static partial class WebhookControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Created webhook {WebhookName} for table {TableName}")]
    public static partial void WebhookCreated(ILogger logger, string webhookName, string tableName);

    [LoggerMessage(LogLevel.Information, "Deleted webhook {WebhookId}")]
    public static partial void WebhookDeleted(ILogger logger, Guid webhookId);

    [LoggerMessage(LogLevel.Information, "Regenerated secret for webhook {WebhookId}")]
    public static partial void SecretRegenerated(ILogger logger, Guid webhookId);
}

/// <summary>
/// Webhook management API endpoints.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[Produces("application/json")]
public sealed class WebhookController : ControllerBase
{
    private readonly IWebhookManager _webhookManager;
    private readonly ISchemaManager _schemaManager;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWebhookManager webhookManager,
        ISchemaManager schemaManager,
        ILogger<WebhookController> logger)
    {
        _webhookManager = webhookManager;
        _schemaManager = schemaManager;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new webhook subscription.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateWebhook(
        [FromBody] CreateWebhookApiRequest request,
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        // Validate table exists
        var table = await _schemaManager.GetTableAsync(tenantId, request.Table, cancellationToken);
        if (table is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = $"Table '{request.Table}' not found"
            });
        }

        var events = request.Events?.Select(ParseEvent).ToList() ?? [WebhookEvent.Insert, WebhookEvent.Update, WebhookEvent.Delete];

        var createRequest = new CreateWebhookRequest
        {
            TenantId = tenantId,
            TableId = table.TableId,
            LogicalName = request.Name,
            Url = request.Url,
            Events = events,
            Filter = request.Filter,
            Headers = request.Headers
        };

        var webhook = await _webhookManager.CreateWebhookAsync(createRequest, cancellationToken);

        WebhookControllerLogs.WebhookCreated(_logger, webhook.LogicalName, request.Table);

        return CreatedAtAction(
            nameof(GetWebhook),
            new { webhookId = webhook.WebhookId },
            MapToResponse(webhook, showSecret: true));
    }

    /// <summary>
    /// Gets a webhook by ID.
    /// </summary>
    [HttpGet("{webhookId:guid}")]
    [ProducesResponseType(typeof(WebhookApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWebhook(Guid webhookId, CancellationToken cancellationToken)
    {
        var webhook = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (webhook is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found"
            });
        }

        return Ok(MapToResponse(webhook, showSecret: false));
    }

    /// <summary>
    /// Lists webhooks for the tenant.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWebhooks(
        [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
        [FromQuery] string? table = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "InvalidTenant",
                Message = "X-Tenant-Id header is required"
            });
        }

        var webhooks = await _webhookManager.ListWebhooksAsync(tenantId, table, cancellationToken);
        return Ok(webhooks.Select(w => MapToResponse(w, showSecret: false)));
    }

    /// <summary>
    /// Updates a webhook.
    /// </summary>
    [HttpPatch("{webhookId:guid}")]
    [ProducesResponseType(typeof(WebhookApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateWebhook(
        Guid webhookId,
        [FromBody] UpdateWebhookApiRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found"
            });
        }

        var updateRequest = new UpdateWebhookRequest
        {
            WebhookId = webhookId,
            Url = request.Url,
            Events = request.Events?.Select(ParseEvent).ToList(),
            Filter = request.Filter,
            Headers = request.Headers,
            IsActive = request.IsActive
        };

        var webhook = await _webhookManager.UpdateWebhookAsync(updateRequest, cancellationToken);
        return Ok(MapToResponse(webhook, showSecret: false));
    }

    /// <summary>
    /// Deletes a webhook.
    /// </summary>
    [HttpDelete("{webhookId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteWebhook(Guid webhookId, CancellationToken cancellationToken)
    {
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found"
            });
        }

        await _webhookManager.DeleteWebhookAsync(webhookId, cancellationToken);

        WebhookControllerLogs.WebhookDeleted(_logger, webhookId);

        return NoContent();
    }

    /// <summary>
    /// Regenerates the webhook secret.
    /// </summary>
    [HttpPost("{webhookId:guid}/regenerate-secret")]
    [ProducesResponseType(typeof(RegenerateSecretResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegenerateSecret(Guid webhookId, CancellationToken cancellationToken)
    {
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found"
            });
        }

        var newSecret = await _webhookManager.RegenerateSecretAsync(webhookId, cancellationToken);

        WebhookControllerLogs.SecretRegenerated(_logger, webhookId);

        return Ok(new RegenerateSecretResponse { Secret = newSecret });
    }

    /// <summary>
    /// Gets delivery history for a webhook.
    /// </summary>
    [HttpGet("{webhookId:guid}/deliveries")]
    [ProducesResponseType(typeof(IReadOnlyList<DeliveryApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeliveryHistory(
        Guid webhookId,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found"
            });
        }

        var deliveries = await _webhookManager.GetDeliveryHistoryAsync(webhookId, limit, offset, cancellationToken);
        return Ok(deliveries.Select(MapToDeliveryResponse));
    }

    private static WebhookEvent ParseEvent(string eventName)
    {
        return eventName.ToLowerInvariant() switch
        {
            "insert" => WebhookEvent.Insert,
            "update" => WebhookEvent.Update,
            "delete" => WebhookEvent.Delete,
            _ => throw new ArgumentException($"Invalid event: {eventName}")
        };
    }

    private static WebhookApiResponse MapToResponse(WebhookMetadata webhook, bool showSecret)
    {
        return new WebhookApiResponse
        {
            Id = webhook.WebhookId,
            Name = webhook.LogicalName,
            Table = webhook.TableLogicalName ?? string.Empty,
            Url = webhook.Url,
            Secret = showSecret ? webhook.Secret : null,
            Events = webhook.Events.Select(e => e.ToString().ToLowerInvariant()).ToList(),
            Filter = webhook.Filter,
            Headers = webhook.Headers,
            IsActive = webhook.IsActive,
            CreatedAt = webhook.CreatedAt,
            UpdatedAt = webhook.UpdatedAt
        };
    }

    private static DeliveryApiResponse MapToDeliveryResponse(WebhookDelivery delivery)
    {
        return new DeliveryApiResponse
        {
            Id = delivery.DeliveryId,
            Event = delivery.Event.ToString().ToLowerInvariant(),
            RecordId = delivery.RecordId,
            Status = delivery.Status.ToString().ToLowerInvariant(),
            AttemptCount = delivery.AttemptCount,
            HttpStatusCode = delivery.HttpStatusCode,
            ErrorMessage = delivery.ErrorMessage,
            CreatedAt = delivery.CreatedAt,
            DeliveredAt = delivery.DeliveredAt
        };
    }
}
