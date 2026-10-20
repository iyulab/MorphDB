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
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListWebhookInfo, cancellationToken) ?? [];
    }

    /// <summary>
    /// Gets a webhook by ID.
    /// </summary>
    public async Task<WebhookInfo?> GetByIdAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/webhooks/{webhookId}", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookInfo, cancellationToken);
    }

    /// <summary>
    /// Gets webhooks for a table.
    /// </summary>
    public async Task<IReadOnlyList<WebhookInfo>> GetByTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/webhooks?table={Uri.EscapeDataString(tableName)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListWebhookInfo, cancellationToken) ?? [];
    }

    /// <summary>
    /// Creates a new webhook.
    /// </summary>
    public async Task<WebhookInfo> CreateAsync(CreateWebhookRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/webhooks", request, MorphDBJsonContext.Default.CreateWebhookRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize webhook response");
    }

    /// <summary>
    /// Deletes a webhook.
    /// </summary>
    public async Task DeleteAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/webhooks/{webhookId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
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
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListWebhookDelivery, cancellationToken) ?? [];
    }

    /// <summary>
    /// Changes a webhook. Members of <paramref name="request"/> left null are left as they are.
    /// </summary>
    public async Task<WebhookInfo> UpdateAsync(Guid webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"/api/webhooks/{webhookId}", request, MorphDBJsonContext.Default.UpdateWebhookRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize webhook response");
    }

    /// <summary>
    /// Replaces a webhook's signing secret and returns the new one — the only time it is returned.
    /// Deliveries are signed with it from now on.
    /// </summary>
    public async Task<string> RegenerateSecretAsync(Guid webhookId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/webhooks/{webhookId}/regenerate-secret", null, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.RegeneratedWebhookSecret, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize secret response");
        return body.Secret;
    }

    /// <summary>
    /// The project's dead letters — deliveries that gave up — newest first, optionally of one webhook
    /// and in one state.
    /// </summary>
    public async Task<IReadOnlyList<WebhookDeadLetter>> GetDeadLettersAsync(
        Guid? webhookId = null,
        WebhookDeadLetterStatus? status = null,
        int limit = 100,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"limit={limit}"),
            string.Create(CultureInfo.InvariantCulture, $"offset={offset}"),
        };
        if (webhookId is { } id)
            query.Add($"webhookId={id}");
        if (status is { } s)
            query.Add($"status={s}");
        var response = await _httpClient.GetAsync($"/api/webhooks/dlq?{string.Join("&", query)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListWebhookDeadLetter, cancellationToken) ?? [];
    }

    /// <summary>
    /// One dead letter of the project, or null.
    /// </summary>
    public async Task<WebhookDeadLetter?> GetDeadLetterAsync(Guid dlqId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/webhooks/dlq/{dlqId}", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookDeadLetter, cancellationToken);
    }

    /// <summary>
    /// Counts over the project's dead letters.
    /// </summary>
    public async Task<WebhookDeadLetterStatistics> GetDeadLetterStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/webhooks/dlq/stats", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookDeadLetterStatistics, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize dead-letter statistics");
    }

    /// <summary>
    /// Closes a dead letter with a note, without delivering it again.
    /// </summary>
    public async Task ResolveDeadLetterAsync(Guid dlqId, string resolutionNotes, Guid? resolvedBy = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/webhooks/dlq/{dlqId}/resolve",
            new ResolveDeadLetterRequest { ResolutionNotes = resolutionNotes, ResolvedBy = resolvedBy },
            MorphDBJsonContext.Default.ResolveDeadLetterRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Delivers a dead letter again, as a new delivery — the way a failed delivery is retried.
    /// </summary>
    public async Task<WebhookDelivery> ReplayDeadLetterAsync(Guid dlqId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/webhooks/dlq/{dlqId}/replay", null, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.WebhookDelivery, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize delivery response");
    }

    /// <summary>
    /// Archives the project's pending and resolved dead letters — of one webhook, and older than a
    /// number of days, when given — and returns how many.
    /// </summary>
    public async Task<int> ArchiveDeadLettersAsync(Guid? webhookId = null, int? olderThanDays = null, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (webhookId is { } id)
            query.Add($"webhookId={id}");
        if (olderThanDays is { } days)
            query.Add(string.Create(CultureInfo.InvariantCulture, $"olderThanDays={days}"));
        var url = query.Count == 0 ? "/api/webhooks/dlq/archive" : $"/api/webhooks/dlq/archive?{string.Join("&", query)}";
        var response = await _httpClient.PostAsync(url, null, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ArchivedDeadLetters, cancellationToken);
        return body?.ArchivedCount ?? 0;
    }
}
