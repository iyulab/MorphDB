using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// Background service that processes pending webhook deliveries with exponential backoff retry.
/// </summary>
public sealed class WebhookProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WebhookProcessorService> _logger;
    private readonly WebhookProcessorOptions _options;

    public WebhookProcessorService(
        IServiceProvider serviceProvider,
        ILogger<WebhookProcessorService> logger,
        WebhookProcessorOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WebhookProcessorServiceLogs.ProcessorStarted(_logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                WebhookProcessorServiceLogs.ProcessorError(_logger, ex);
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        WebhookProcessorServiceLogs.ProcessorStopped(_logger);
    }

    private async Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var webhookManager = scope.ServiceProvider.GetRequiredService<IWebhookManager>();
        var deliveryService = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryService>();

        var pendingDeliveries = await webhookManager.GetPendingDeliveriesAsync(
            _options.BatchSize,
            cancellationToken);

        if (pendingDeliveries.Count == 0)
        {
            return;
        }

        WebhookProcessorServiceLogs.ProcessingBatch(_logger, pendingDeliveries.Count);

        foreach (var delivery in pendingDeliveries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessDeliveryAsync(
                webhookManager,
                deliveryService,
                delivery,
                cancellationToken);
        }
    }

    private async Task ProcessDeliveryAsync(
        IWebhookManager webhookManager,
        IWebhookDeliveryService deliveryService,
        WebhookDelivery delivery,
        CancellationToken cancellationToken)
    {
        var webhook = await webhookManager.GetWebhookAsync(delivery.WebhookId, cancellationToken);
        if (webhook is null || !webhook.IsActive)
        {
            WebhookProcessorServiceLogs.WebhookInactive(_logger, delivery.DeliveryId, delivery.WebhookId);

            // Move to DLQ with appropriate reason
            var dlqReason = webhook is null ? DlqReason.WebhookDeleted : DlqReason.WebhookInactive;
            await MoveToDlqAsync(webhookManager, delivery, dlqReason, cancellationToken);

            return;
        }

        var result = await deliveryService.DeliverAsync(webhook, delivery, cancellationToken);
        var newAttemptCount = delivery.AttemptCount + 1;

        if (result.Success)
        {
            await webhookManager.UpdateDeliveryAsync(new UpdateDeliveryRequest
            {
                DeliveryId = delivery.DeliveryId,
                Status = DeliveryStatus.Success,
                AttemptCount = newAttemptCount,
                HttpStatusCode = result.HttpStatusCode,
                ResponseBody = result.ResponseBody,
                DeliveredAt = DateTimeOffset.UtcNow
            }, cancellationToken);
        }
        else if (newAttemptCount >= _options.MaxRetries)
        {
            WebhookProcessorServiceLogs.MaxRetriesExceeded(_logger, delivery.DeliveryId, newAttemptCount);

            // Determine DLQ reason based on failure type
            var dlqReason = DeterminesDlqReason(result);

            // Update attempt count first
            await webhookManager.UpdateDeliveryAsync(new UpdateDeliveryRequest
            {
                DeliveryId = delivery.DeliveryId,
                Status = DeliveryStatus.Failed,
                AttemptCount = newAttemptCount,
                HttpStatusCode = result.HttpStatusCode,
                ResponseBody = result.ResponseBody,
                ErrorMessage = result.ErrorMessage ?? "Max retries exceeded"
            }, cancellationToken);

            // Move to Dead Letter Queue
            await MoveToDlqAsync(webhookManager, delivery, dlqReason, cancellationToken);
        }
        else
        {
            // Check for persistent 4xx client errors (excluding 429 Too Many Requests)
            if (result.HttpStatusCode is >= 400 and < 500 and not 429)
            {
                WebhookProcessorServiceLogs.PersistentClientError(_logger, delivery.DeliveryId, result.HttpStatusCode ?? 0);

                await webhookManager.UpdateDeliveryAsync(new UpdateDeliveryRequest
                {
                    DeliveryId = delivery.DeliveryId,
                    Status = DeliveryStatus.Failed,
                    AttemptCount = newAttemptCount,
                    HttpStatusCode = result.HttpStatusCode,
                    ResponseBody = result.ResponseBody,
                    ErrorMessage = result.ErrorMessage ?? "Persistent client error"
                }, cancellationToken);

                // Move to DLQ immediately for 4xx errors
                await MoveToDlqAsync(webhookManager, delivery, DlqReason.PersistentClientError, cancellationToken);
                return;
            }

            var nextRetry = CalculateNextRetryTime(newAttemptCount);
            WebhookProcessorServiceLogs.SchedulingRetry(_logger, delivery.DeliveryId, newAttemptCount, nextRetry);

            await webhookManager.UpdateDeliveryAsync(new UpdateDeliveryRequest
            {
                DeliveryId = delivery.DeliveryId,
                Status = DeliveryStatus.Retrying,
                AttemptCount = newAttemptCount,
                HttpStatusCode = result.HttpStatusCode,
                ResponseBody = result.ResponseBody,
                ErrorMessage = result.ErrorMessage,
                NextRetryAt = nextRetry
            }, cancellationToken);
        }
    }

    private async Task MoveToDlqAsync(
        IWebhookManager webhookManager,
        WebhookDelivery delivery,
        DlqReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var dlqMessage = await webhookManager.MoveToDlqAsync(new MoveToDlqRequest
            {
                DeliveryId = delivery.DeliveryId,
                Reason = reason
            }, cancellationToken);

            WebhookProcessorServiceLogs.MovedToDlq(_logger, delivery.DeliveryId, dlqMessage.DlqId, reason.ToString());
        }
        catch (Exception ex)
        {
            WebhookProcessorServiceLogs.DlqMoveFailed(_logger, delivery.DeliveryId, ex);
        }
    }

    private static DlqReason DeterminesDlqReason(DeliveryResult result)
    {
        if (result.HttpStatusCode is >= 400 and < 500)
            return DlqReason.PersistentClientError;

        return DlqReason.MaxRetriesExceeded;
    }

    /// <summary>
    /// Calculates the next retry time using exponential backoff with jitter.
    /// Base delay: 5 seconds, multiplied by 5^(attempt-1), plus random jitter.
    /// Attempt 1: ~5s, Attempt 2: ~25s, Attempt 3: ~125s, Attempt 4: ~625s
    /// </summary>
    private DateTimeOffset CalculateNextRetryTime(int attemptCount)
    {
        // Exponential backoff: 5 * 5^(n-1) seconds
        var baseDelay = TimeSpan.FromSeconds(_options.BaseRetryDelaySeconds);
        var multiplier = Math.Pow(_options.RetryMultiplier, attemptCount - 1);
        var delay = TimeSpan.FromSeconds(baseDelay.TotalSeconds * multiplier);

        // Cap at max delay
        if (delay > _options.MaxRetryDelay)
        {
            delay = _options.MaxRetryDelay;
        }

        // Add jitter (0-25% of delay)
        var jitter = TimeSpan.FromSeconds(delay.TotalSeconds * Random.Shared.NextDouble() * 0.25);

        return DateTimeOffset.UtcNow.Add(delay).Add(jitter);
    }
}

/// <summary>
/// Options for the webhook processor background service.
/// </summary>
public sealed class WebhookProcessorOptions
{
    /// <summary>
    /// Interval between polling for pending deliveries.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of deliveries to process per batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay for first retry in seconds.
    /// </summary>
    public int BaseRetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Multiplier for exponential backoff.
    /// </summary>
    public double RetryMultiplier { get; set; } = 5.0;

    /// <summary>
    /// Maximum delay between retries.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Timeout for individual HTTP requests.
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal static partial class WebhookProcessorServiceLogs
{
    [LoggerMessage(LogLevel.Information, "Webhook processor service started")]
    public static partial void ProcessorStarted(ILogger logger);

    [LoggerMessage(LogLevel.Information, "Webhook processor service stopped")]
    public static partial void ProcessorStopped(ILogger logger);

    [LoggerMessage(LogLevel.Error, "Webhook processor error")]
    public static partial void ProcessorError(ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Debug, "Processing batch of {Count} pending deliveries")]
    public static partial void ProcessingBatch(ILogger logger, int count);

    [LoggerMessage(LogLevel.Warning, "Delivery {DeliveryId} skipped: webhook {WebhookId} is inactive")]
    public static partial void WebhookInactive(ILogger logger, Guid deliveryId, Guid webhookId);

    [LoggerMessage(LogLevel.Warning, "Delivery {DeliveryId} failed after {AttemptCount} attempts, max retries exceeded")]
    public static partial void MaxRetriesExceeded(ILogger logger, Guid deliveryId, int attemptCount);

    [LoggerMessage(LogLevel.Debug, "Delivery {DeliveryId} attempt {AttemptCount} failed, scheduling retry at {NextRetry}")]
    public static partial void SchedulingRetry(ILogger logger, Guid deliveryId, int attemptCount, DateTimeOffset nextRetry);

    [LoggerMessage(LogLevel.Warning, "Delivery {DeliveryId} received persistent client error (HTTP {StatusCode}), moving to DLQ")]
    public static partial void PersistentClientError(ILogger logger, Guid deliveryId, int statusCode);

    [LoggerMessage(LogLevel.Information, "Delivery {DeliveryId} moved to DLQ {DlqId} (reason: {Reason})")]
    public static partial void MovedToDlq(ILogger logger, Guid deliveryId, Guid dlqId, string reason);

    [LoggerMessage(LogLevel.Error, "Failed to move delivery {DeliveryId} to DLQ")]
    public static partial void DlqMoveFailed(ILogger logger, Guid deliveryId, Exception exception);
}
