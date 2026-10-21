using System.Globalization;
using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for reading a project's audit log. The project is named on each call, as the routes name it;
/// a secret confined to one project reads only that project's log.
/// </summary>
public sealed class AuditClient
{
    private readonly HttpClient _httpClient;

    internal AuditClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// A page of the project's audit log, newest first unless <see cref="AuditLogQuery.Descending"/> is false.
    /// </summary>
    public async Task<AuditLogPage> QueryLogsAsync(Guid projectId, AuditLogQuery? query = null, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/audit/logs{QueryString(query)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.AuditLogPage, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize audit log page");
    }

    /// <summary>
    /// One entry of the project's audit log, or null.
    /// </summary>
    public async Task<AuditLogEntry?> GetLogAsync(Guid projectId, Guid logId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/audit/logs/{logId}", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.AuditLogEntry, cancellationToken);
    }

    /// <summary>
    /// Counts over the project's audit log between <paramref name="from"/> and <paramref name="to"/>.
    /// </summary>
    public async Task<AuditStats> GetStatsAsync(Guid projectId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();
        if (from is { } f)
            parts.Add($"from={Uri.EscapeDataString(Instant(f))}");
        if (to is { } t)
            parts.Add($"to={Uri.EscapeDataString(Instant(t))}");
        var query = parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/audit/stats{query}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.AuditStats, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize audit statistics");
    }

    private static string QueryString(AuditLogQuery? query)
    {
        if (query is null)
            return string.Empty;

        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (value is not null)
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }

        Add("category", query.Category?.ToString().ToLowerInvariant());
        Add("minSeverity", query.MinSeverity?.ToString().ToLowerInvariant());
        Add("actorId", query.ActorId);
        Add("resourceType", query.ResourceType);
        Add("resourceId", query.ResourceId);
        Add("action", query.Action);
        Add("from", query.From is { } from ? Instant(from) : null);
        Add("to", query.To is { } to ? Instant(to) : null);
        Add("searchText", query.SearchText);
        Add("page", query.Page?.ToString(CultureInfo.InvariantCulture));
        Add("pageSize", query.PageSize?.ToString(CultureInfo.InvariantCulture));
        Add("orderBy", query.OrderBy);
        Add("descending", query.Descending is { } descending ? (descending ? "true" : "false") : null);
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string Instant(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
