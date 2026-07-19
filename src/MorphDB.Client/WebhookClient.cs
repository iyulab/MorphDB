using System.Globalization;
using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for webhook operations.
/// </summary>
public sealed class WebhookClient
{
    private readonly HttpClient _httpClient;

    internal WebhookClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets all webhooks.
    /// </summary>
    public async Task<IReadOnlyList<WebhookInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/webhooks", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<WebhookInfo>>(MorphDBJson.Options, cancellationToken) ?? [];
    }

    /// <summary>
    /// Gets a webhook by ID.
    /// </summary>
    public async Task<WebhookInfo?> GetByIdAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/webhooks/{webhookId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WebhookInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Gets webhooks for a table.
    /// </summary>
    public async Task<IReadOnlyList<WebhookInfo>> GetByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/webhooks?tableName={Uri.EscapeDataString(tableName)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<WebhookInfo>>(MorphDBJson.Options, cancellationToken) ?? [];
    }

    /// <summary>
    /// Creates a new webhook.
    /// </summary>
    public async Task<WebhookInfo> CreateAsync(CreateWebhookRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/webhooks", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<WebhookInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize webhook response");
    }

    /// <summary>
    /// Deletes a webhook.
    /// </summary>
    public async Task DeleteAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/webhooks/{webhookId}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Gets webhook deliveries.
    /// </summary>
    public async Task<IReadOnlyList<WebhookDelivery>> GetDeliveriesAsync(
        Guid webhookId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture, $"/api/webhooks/{webhookId}/deliveries?page={page}&pageSize={pageSize}");
        var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<WebhookDelivery>>(MorphDBJson.Options, cancellationToken) ?? [];
    }

    /// <summary>
    /// Retries a failed delivery.
    /// </summary>
    public async Task RetryDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/webhooks/deliveries/{deliveryId}/retry", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw response.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => new MorphDBNotFoundException($"Resource not found: {response.RequestMessage?.RequestUri}", body),
                System.Net.HttpStatusCode.BadRequest => new MorphDBValidationException($"Validation failed", responseBody: body),
                System.Net.HttpStatusCode.Unauthorized => new MorphDBAuthenticationException("Authentication required", body),
                System.Net.HttpStatusCode.Forbidden => new MorphDBAuthorizationException("Access denied", body),
                System.Net.HttpStatusCode.Conflict => new MorphDBConflictException("Resource conflict", body),
                _ => new MorphDBApiException($"API request failed: {response.ReasonPhrase}", response.StatusCode, responseBody: body)
            };
        }
    }
}
