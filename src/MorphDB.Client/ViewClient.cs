using System.Globalization;
using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for view operations.
/// </summary>
public sealed class ViewClient
{
    private readonly HttpClient _httpClient;

    internal ViewClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Lists all views.
    /// </summary>
    public async Task<IReadOnlyList<ViewInfo>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/views", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.IReadOnlyListViewInfo, cancellationToken)
            ?? [];
    }

    /// <summary>
    /// Gets a view by name.
    /// </summary>
    public async Task<ViewInfo?> GetAsync(
        string viewName,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/views/{Uri.EscapeDataString(viewName)}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ViewInfo, cancellationToken);
    }

    /// <summary>
    /// Creates a new view.
    /// </summary>
    public async Task<ViewInfo> CreateAsync(
        CreateViewRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/views", request, MorphDBJsonContext.Default.CreateViewRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ViewInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize view response");
    }

    /// <summary>
    /// Deletes a view.
    /// </summary>
    public async Task DeleteAsync(
        string viewName,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/views/{Uri.EscapeDataString(viewName)}",
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Refreshes a materialized view.
    /// </summary>
    public async Task RefreshAsync(
        string viewName,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/views/{Uri.EscapeDataString(viewName)}/refresh",
            null,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Queries data from a view — the same selection, filters, order and paging a table read takes.
    /// </summary>
    public async Task<ViewRows> QueryAsync(
        string viewName,
        QueryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/views/{Uri.EscapeDataString(viewName)}/data{BuildQueryString(request)}",
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ViewRows, cancellationToken)
            ?? new ViewRows();
    }

    /// <summary>
    /// A view read's query string. The view endpoint takes its filters as one <c>AND</c>-joined
    /// parameter and pages by <c>skip</c>/<c>take</c>, where a table read repeats <c>filter</c> and
    /// pages by <c>page</c>/<c>pageSize</c>/<c>offset</c>.
    /// </summary>
    private static string BuildQueryString(QueryRequest? request)
    {
        if (request == null)
            return string.Empty;

        var parts = new List<string>();

        if (request.Select.Count > 0)
            parts.Add($"select={Uri.EscapeDataString(string.Join(",", request.Select))}");

        if (request.Filters.Count > 0)
        {
            var filters = request.Filters.Select(f =>
                $"{f.Column}:{DataClient.GetOperatorString(f.Operator)}:{DataClient.FormatFilterValue(f.Value)}");
            parts.Add($"filter={Uri.EscapeDataString(string.Join(" AND ", filters))}");
        }

        if (request.OrderBy.Count > 0)
        {
            var orderParts = request.OrderBy.Select(o => $"{o.Column}:{(o.Ascending ? "asc" : "desc")}");
            parts.Add($"orderBy={Uri.EscapeDataString(string.Join(",", orderParts))}");
        }

        var skip = request.Offset ?? (request.Page - 1) * request.PageSize;
        if (skip > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"skip={skip}"));

        parts.Add(string.Create(CultureInfo.InvariantCulture, $"take={request.PageSize}"));

        return "?" + string.Join("&", parts);
    }

}
