using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Services;

/// <summary>
/// HTTP-based webhook delivery service.
/// </summary>
public sealed class WebhookDeliveryService : IWebhookDeliveryService
{
    private readonly HttpClient _httpClient;
    private readonly IWebhookManager _webhookManager;
    private readonly WebhookSignatureService _signatureService;
    private readonly ILogger<WebhookDeliveryService> _logger;

    private const string SignatureHeader = "X-MorphDB-Signature";
    private const string TimestampHeader = "X-MorphDB-Timestamp";
    private const string DeliveryIdHeader = "X-MorphDB-Delivery-Id";
    private const string EventHeader = "X-MorphDB-Event";

    public WebhookDeliveryService(
        HttpClient httpClient,
        IWebhookManager webhookManager,
        WebhookSignatureService signatureService,
        ILogger<WebhookDeliveryService> logger)
    {
        _httpClient = httpClient;
        _webhookManager = webhookManager;
        _signatureService = signatureService;
        _logger = logger;
    }

    public async Task QueueDeliveryAsync(
        WebhookMetadata webhook,
        WebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var payloadJson = JsonDocument.Parse(
            JsonSerializer.Serialize(payload, WebhookPayloadSerialization.Options));

        var request = new CreateDeliveryRequest
        {
            WebhookId = webhook.WebhookId,
            RecordId = payload.RecordId,
            Event = ParseEvent(payload.Event),
            Payload = payloadJson
        };

        await _webhookManager.CreateDeliveryAsync(request, cancellationToken);

        WebhookDeliveryServiceLogs.DeliveryQueued(_logger, webhook.WebhookId, payload.Event.ToString());
    }

    public async Task<DeliveryResult> DeliverAsync(
        WebhookMetadata webhook,
        WebhookDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Serialize payload
            var payloadJson = delivery.Payload.RootElement.GetRawText();
            var timestamp = _signatureService.GetCurrentTimestamp();

            // Compute signature
            var signature = _signatureService.ComputeSignature(webhook.Secret, timestamp, payloadJson);

            // Create HTTP request
            using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            // Add MorphDB headers
            request.Headers.Add(SignatureHeader, signature);
            request.Headers.Add(TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add(DeliveryIdHeader, delivery.DeliveryId.ToString());
            request.Headers.Add(EventHeader, delivery.Event.ToString().ToLowerInvariant());

            // Add custom headers from webhook configuration
            if (webhook.Headers is not null)
            {
                foreach (var header in webhook.Headers.RootElement.EnumerateObject())
                {
                    if (header.Value.ValueKind == JsonValueKind.String)
                    {
                        request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
                    }
                }
            }

            // Send request
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            stopwatch.Stop();

            var success = response.IsSuccessStatusCode;

            if (success)
            {
                WebhookDeliveryServiceLogs.DeliverySucceeded(
                    _logger,
                    delivery.DeliveryId,
                    (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds);
            }
            else
            {
                WebhookDeliveryServiceLogs.DeliveryFailed(
                    _logger,
                    delivery.DeliveryId,
                    (int)response.StatusCode,
                    responseBody);
            }

            return new DeliveryResult
            {
                Success = success,
                HttpStatusCode = (int)response.StatusCode,
                ResponseBody = TruncateResponse(responseBody),
                Duration = stopwatch.Elapsed
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            WebhookDeliveryServiceLogs.DeliveryException(_logger, delivery.DeliveryId, ex);

            return new DeliveryResult
            {
                Success = false,
                ErrorMessage = $"HTTP error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            stopwatch.Stop();
            WebhookDeliveryServiceLogs.DeliveryTimeout(_logger, delivery.DeliveryId);

            return new DeliveryResult
            {
                Success = false,
                ErrorMessage = "Request timed out",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            WebhookDeliveryServiceLogs.DeliveryException(_logger, delivery.DeliveryId, ex);

            return new DeliveryResult
            {
                Success = false,
                ErrorMessage = $"Unexpected error: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }

    private static string? TruncateResponse(string? response, int maxLength = 1000)
    {
        if (response is null || response.Length <= maxLength)
        {
            return response;
        }

        return response[..maxLength] + "... (truncated)";
    }

    private static WebhookEvent ParseEvent(string eventName)
    {
        return eventName.ToLowerInvariant() switch
        {
            "insert" => WebhookEvent.Insert,
            "update" => WebhookEvent.Update,
            "delete" => WebhookEvent.Delete,
            _ => throw new ArgumentException($"Unknown event: {eventName}")
        };
    }
}

internal static partial class WebhookDeliveryServiceLogs
{
    [LoggerMessage(LogLevel.Debug, "Queued delivery for webhook {WebhookId}, event {Event}")]
    public static partial void DeliveryQueued(ILogger logger, Guid webhookId, string @event);

    [LoggerMessage(LogLevel.Information, "Delivery {DeliveryId} succeeded with status {StatusCode} in {DurationMs}ms")]
    public static partial void DeliverySucceeded(ILogger logger, Guid deliveryId, int statusCode, long durationMs);

    [LoggerMessage(LogLevel.Warning, "Delivery {DeliveryId} failed with status {StatusCode}: {ResponseBody}")]
    public static partial void DeliveryFailed(ILogger logger, Guid deliveryId, int statusCode, string? responseBody);

    [LoggerMessage(LogLevel.Warning, "Delivery {DeliveryId} timed out")]
    public static partial void DeliveryTimeout(ILogger logger, Guid deliveryId);

    [LoggerMessage(LogLevel.Error, "Delivery {DeliveryId} threw exception")]
    public static partial void DeliveryException(ILogger logger, Guid deliveryId, Exception exception);
}
