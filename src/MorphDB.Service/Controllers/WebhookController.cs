using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

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
[RequireProject]
public sealed class WebhookController : ControllerBase
{
    private readonly IWebhookManager _webhookManager;
    private readonly ISchemaManager _schemaManager;
    private readonly ILogger<WebhookController> _logger;
    private readonly IProjectContextAccessor _projectContext;

    public WebhookController(
        IWebhookManager webhookManager,
        ISchemaManager schemaManager,
        ILogger<WebhookController> logger,
        IProjectContextAccessor projectContext)
    {
        _webhookManager = webhookManager;
        _schemaManager = schemaManager;
        _logger = logger;
        _projectContext = projectContext;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

    /// <summary>
    /// Creates a new webhook subscription.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WebhookApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateWebhook(
        [FromBody] CreateWebhookApiRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();

        // Validate table exists
        var table = await _schemaManager.GetTableAsync(projectId, request.Table, cancellationToken);
        if (table is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "TableNotFound",
                Message = $"Table '{request.Table}' not found",
                Code = "TABLE_NOT_FOUND"
            });
        }

        var events = request.Events?.Select(ParseEvent).ToList() ?? [WebhookEvent.Insert, WebhookEvent.Update, WebhookEvent.Delete];

        var createRequest = new CreateWebhookRequest
        {
            ProjectId = projectId,
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
    public async Task<IActionResult> GetWebhook(
        Guid webhookId,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var webhook = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (webhook is null || webhook.ProjectId != projectId)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found",
                Code = "WEBHOOK_NOT_FOUND"
            });
        }

        return Ok(MapToResponse(webhook, showSecret: false));
    }

    /// <summary>
    /// Lists webhooks for the project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWebhooks(
        [FromQuery] string? table = null,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var webhooks = await _webhookManager.ListWebhooksAsync(projectId, table, cancellationToken);
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
        var projectId = GetProjectId();
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found",
                Code = "WEBHOOK_NOT_FOUND"
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
    public async Task<IActionResult> DeleteWebhook(
        Guid webhookId,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found",
                Code = "WEBHOOK_NOT_FOUND"
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
    public async Task<IActionResult> RegenerateSecret(
        Guid webhookId,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found",
                Code = "WEBHOOK_NOT_FOUND"
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
        var projectId = GetProjectId();
        var existing = await _webhookManager.GetWebhookAsync(webhookId, cancellationToken);
        if (existing is null || existing.ProjectId != projectId)
        {
            return NotFound(new ErrorResponse
            {
                Error = "WebhookNotFound",
                Message = $"Webhook '{webhookId}' not found",
                Code = "WEBHOOK_NOT_FOUND"
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

    #region Dead Letter Queue Endpoints

    /// <summary>
    /// Lists DLQ messages for a webhook or all webhooks.
    /// </summary>
    [HttpGet("dlq")]
    [ProducesResponseType(typeof(IEnumerable<DlqMessageApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDlqMessages(
        [FromQuery] Guid? webhookId,
        [FromQuery] string? status,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        DlqStatus? dlqStatus = status is not null
            ? Enum.Parse<DlqStatus>(status, ignoreCase: true)
            : null;

        var messages = await _webhookManager.ListDlqMessagesAsync(webhookId, dlqStatus, limit, offset, cancellationToken);
        return Ok(messages.Select(MapToDlqResponse));
    }

    /// <summary>
    /// Gets DLQ statistics for a project.
    /// </summary>
    [HttpGet("dlq/stats")]
    [ProducesResponseType(typeof(DlqStatisticsApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDlqStatistics(
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();
        var stats = await _webhookManager.GetDlqStatisticsAsync(projectId, cancellationToken);
        return Ok(new DlqStatisticsApiResponse
        {
            TotalMessages = stats.TotalMessages,
            PendingReviewCount = stats.PendingReviewCount,
            ResolvedCount = stats.ResolvedCount,
            ArchivedCount = stats.ArchivedCount,
            ByReason = stats.ByReason,
            ByWebhook = stats.ByWebhook,
            OldestPendingAt = stats.OldestPendingAt
        });
    }

    /// <summary>
    /// Gets a specific DLQ message.
    /// </summary>
    [HttpGet("dlq/{dlqId:guid}")]
    [ProducesResponseType(typeof(DlqMessageApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDlqMessage(
        Guid dlqId,
        CancellationToken cancellationToken = default)
    {
        var message = await _webhookManager.GetDlqMessageAsync(dlqId, cancellationToken);
        if (message is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"DLQ message {dlqId} not found",
                Code = "NOT_FOUND"
            });
        }

        return Ok(MapToDlqResponse(message));
    }

    /// <summary>
    /// Resolves a DLQ message.
    /// </summary>
    [HttpPost("dlq/{dlqId:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveDlqMessage(
        Guid dlqId,
        [FromBody] ResolveDlqApiRequest request,
        CancellationToken cancellationToken = default)
    {
        var message = await _webhookManager.GetDlqMessageAsync(dlqId, cancellationToken);
        if (message is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"DLQ message {dlqId} not found",
                Code = "NOT_FOUND"
            });
        }

        await _webhookManager.ResolveDlqMessageAsync(new ResolveDlqRequest
        {
            DlqId = dlqId,
            ResolutionNotes = request.ResolutionNotes,
            ResolvedBy = request.ResolvedBy
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Replays a DLQ message (creates a new delivery attempt).
    /// </summary>
    [HttpPost("dlq/{dlqId:guid}/replay")]
    [ProducesResponseType(typeof(DeliveryApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayDlqMessage(
        Guid dlqId,
        CancellationToken cancellationToken = default)
    {
        var message = await _webhookManager.GetDlqMessageAsync(dlqId, cancellationToken);
        if (message is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"DLQ message {dlqId} not found",
                Code = "NOT_FOUND"
            });
        }

        var delivery = await _webhookManager.ReplayDlqMessageAsync(dlqId, cancellationToken);
        return Ok(MapToDeliveryResponse(delivery));
    }

    /// <summary>
    /// Archives old DLQ messages.
    /// </summary>
    [HttpPost("dlq/archive")]
    [ProducesResponseType(typeof(ArchiveDlqApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ArchiveDlqMessages(
        [FromQuery] Guid? webhookId,
        [FromQuery] int? olderThanDays,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? olderThan = olderThanDays.HasValue
            ? DateTimeOffset.UtcNow.AddDays(-olderThanDays.Value)
            : null;

        var archivedCount = await _webhookManager.ArchiveDlqMessagesAsync(webhookId, olderThan, cancellationToken);
        return Ok(new ArchiveDlqApiResponse { ArchivedCount = archivedCount });
    }

    private static DlqMessageApiResponse MapToDlqResponse(WebhookDlqMessage message)
    {
        return new DlqMessageApiResponse
        {
            DlqId = message.DlqId,
            DeliveryId = message.DeliveryId,
            WebhookId = message.WebhookId,
            RecordId = message.RecordId,
            Event = message.Event.ToString().ToLowerInvariant(),
            Reason = message.Reason.ToString(),
            AttemptCount = message.AttemptCount,
            LastHttpStatusCode = message.LastHttpStatusCode,
            LastErrorMessage = message.LastErrorMessage,
            Status = message.Status.ToString(),
            ResolutionNotes = message.ResolutionNotes,
            DlqAt = message.DlqAt,
            ResolvedAt = message.ResolvedAt
        };
    }

    #endregion
}
