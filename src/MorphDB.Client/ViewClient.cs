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
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ViewInfo>>(MorphDBJson.Options, cancellationToken)
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
        return await response.Content.ReadFromJsonAsync<ViewInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Creates a new view.
    /// </summary>
    public async Task<ViewInfo> CreateAsync(
        CreateViewRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/views", request, MorphDBJson.Options, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ViewInfo>(MorphDBJson.Options, cancellationToken)
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
    /// Queries data from a view.
    /// </summary>
    public async Task<PagedResponse<DataRecord>> QueryAsync(
        string viewName,
        QueryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var queryString = request != null ? $"?page={request.Page}&pageSize={request.PageSize}" : "";
        var response = await _httpClient.GetAsync(
            $"/api/views/{Uri.EscapeDataString(viewName)}/data{queryString}",
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PagedResponse<DataRecord>>(MorphDBJson.Options, cancellationToken)
            ?? new PagedResponse<DataRecord> { Data = [], Pagination = new PaginationInfo() };
    }

}
