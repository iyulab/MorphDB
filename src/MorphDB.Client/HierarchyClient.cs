using System.Globalization;
using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for walking a table whose rows point at a parent row — the <c>_parent_id</c> system column,
/// or any stored column holding a parent's <c>_id</c>, named by <c>parentColumn</c>.
/// <para>
/// A walk reads only the rows the caller may read, as every other read does. Arguments left null take
/// the service's defaults: <c>_parent_id</c>, a depth of 100, the table's own order.
/// </para>
/// </summary>
public sealed class HierarchyClient
{
    private readonly HttpClient _httpClient;

    internal HierarchyClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// The record's parent, grandparent, and so on — nearest first.
    /// </summary>
    public Task<HierarchyResult> GetAncestorsAsync(
        string table,
        Guid id,
        string? parentColumn = null,
        int? maxDepth = null,
        bool includeSelf = false,
        CancellationToken cancellationToken = default)
        => WalkAsync(table, id, "ancestors", Query(parentColumn, maxDepth, includeSelf, orderBy: null), cancellationToken);

    /// <summary>
    /// The record's children, grandchildren, and so on — nearest first, each depth in <paramref name="orderBy"/>
    /// (<c>column [asc|desc]</c>, comma-separated).
    /// </summary>
    public Task<HierarchyResult> GetDescendantsAsync(
        string table,
        Guid id,
        string? parentColumn = null,
        int? maxDepth = null,
        bool includeSelf = false,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
        => WalkAsync(table, id, "descendants", Query(parentColumn, maxDepth, includeSelf, orderBy), cancellationToken);

    /// <summary>
    /// The record and its descendants, each depth in <paramref name="orderBy"/>.
    /// </summary>
    public Task<HierarchyResult> GetSubtreeAsync(
        string table,
        Guid id,
        string? parentColumn = null,
        int? maxDepth = null,
        string? orderBy = null,
        CancellationToken cancellationToken = default)
        => WalkAsync(table, id, "subtree", Query(parentColumn, maxDepth, includeSelf: false, orderBy), cancellationToken);

    /// <summary>
    /// The record up to its root — root first. Each row's <see cref="HierarchyRecord.Path"/> holds the ids
    /// from the record up to it.
    /// </summary>
    public Task<HierarchyResult> GetPathAsync(
        string table,
        Guid id,
        string? parentColumn = null,
        int? maxDepth = null,
        CancellationToken cancellationToken = default)
        => WalkAsync(table, id, "path", Query(parentColumn, maxDepth, includeSelf: false, orderBy: null), cancellationToken);

    /// <summary>
    /// The records with the same parent; roots are siblings of each other.
    /// </summary>
    public Task<HierarchyResult> GetSiblingsAsync(
        string table,
        Guid id,
        string? parentColumn = null,
        bool includeSelf = false,
        CancellationToken cancellationToken = default)
        => WalkAsync(table, id, "siblings", Query(parentColumn, maxDepth: null, includeSelf, orderBy: null), cancellationToken);

    /// <summary>
    /// Whether giving <see cref="CycleCheckRequest.RecordId"/> the parent <see cref="CycleCheckRequest.NewParentId"/>
    /// would make a cycle.
    /// </summary>
    public async Task<bool> WouldCreateCycleAsync(string table, CycleCheckRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/hierarchy/{Uri.EscapeDataString(table)}/check-cycle", request, MorphDBJsonContext.Default.CycleCheckRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.CycleCheckResult, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize cycle check response");
        return body.WouldCreateCycle;
    }

    /// <summary>
    /// The records already in a cycle.
    /// </summary>
    public async Task<CycleDetectionResult> DetectCyclesAsync(string table, string? parentColumn = null, CancellationToken cancellationToken = default)
    {
        var query = parentColumn is null ? string.Empty : $"?parentColumn={Uri.EscapeDataString(parentColumn)}";
        var response = await _httpClient.GetAsync($"/api/hierarchy/{Uri.EscapeDataString(table)}/detect-cycles{query}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.CycleDetectionResult, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize cycle detection response");
    }

    private async Task<HierarchyResult> WalkAsync(string table, Guid id, string walk, string query, CancellationToken cancellationToken)
    {
        var url = $"/api/hierarchy/{Uri.EscapeDataString(table)}/{id}/{walk}{query}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.HierarchyResult, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize hierarchy response");
    }

    private static string Query(string? parentColumn, int? maxDepth, bool includeSelf, string? orderBy)
    {
        var parts = new List<string>();
        if (parentColumn is not null)
            parts.Add($"parentColumn={Uri.EscapeDataString(parentColumn)}");
        if (maxDepth is { } depth)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"maxDepth={depth}"));
        if (includeSelf)
            parts.Add("includeSelf=true");
        if (orderBy is not null)
            parts.Add($"orderBy={Uri.EscapeDataString(orderBy)}");
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
