using System.Net;
using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for the project lifecycle: create, read, update, delete, and the two reports a project
/// can produce about itself.
/// <para>
/// Every other client on <see cref="MorphDBClient"/> works <i>inside</i> one project and says which
/// one through the <c>X-Project-Id</c> header. These calls are about projects rather than within
/// one: they name the project in the route, and they work the same whether or not the client was
/// given a project id — including <see cref="CreateAsync"/>, which necessarily runs before any id
/// exists to send.
/// </para>
/// </summary>
public sealed class ProjectClient
{
    private readonly HttpClient _httpClient;

    internal ProjectClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Creates a project.
    /// </summary>
    /// <exception cref="MorphDBConflictException">
    /// The requested id or slug is already taken.
    /// </exception>
    public async Task<ProjectInfo> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/projects", request, MorphDBJson.Options, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProjectInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize project response");
    }

    /// <summary>
    /// Lists projects.
    /// <para>
    /// The pagination block's total is the size of the page returned, not the number of projects
    /// that exist — the server does not count them. Page until a page comes back short rather than
    /// dividing the total by the page size.
    /// </para>
    /// </summary>
    /// <param name="status">Lifecycle status to filter by. Null lists every status.</param>
    /// <param name="page">Page number, 1-based.</param>
    /// <param name="pageSize">Rows per page. The server clamps this to 100.</param>
    public async Task<PagedResponse<ProjectInfo>> ListAsync(
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
        };
        if (!string.IsNullOrEmpty(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        var response = await _httpClient.GetAsync($"/api/projects?{string.Join('&', query)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PagedResponse<ProjectInfo>>(MorphDBJson.Options, cancellationToken)
            ?? new PagedResponse<ProjectInfo> { Data = [], Pagination = new PaginationInfo() };
    }

    /// <summary>
    /// Gets a project by id, or null when there is none.
    /// </summary>
    public async Task<ProjectInfo?> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProjectInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Gets a project by slug, or null when there is none.
    /// </summary>
    public async Task<ProjectInfo?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/projects/slug/{Uri.EscapeDataString(slug)}",
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProjectInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Updates a project's name, its settings, or both.
    /// <para>
    /// Settings are stored by replacement — see <see cref="UpdateProjectRequest.Settings"/>. The
    /// name is not: leaving it null keeps the current one.
    /// </para>
    /// </summary>
    public async Task<ProjectInfo> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"/api/projects/{projectId}",
            request,
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProjectInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize project response");
    }

    /// <summary>
    /// Deletes a project and the schemas it owns. Destructive: the data goes with it.
    /// </summary>
    public async Task DeleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/projects/{projectId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Gets size and object counts for a project's schemas.
    /// </summary>
    public async Task<ProjectStats> GetStatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/stats", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ProjectStats>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize project statistics response");
    }

    /// <summary>
    /// Checks a project's physical schemas against its metadata and reports what disagrees.
    /// </summary>
    public async Task<SchemaHealthReport> GetHealthAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/projects/{projectId}/health", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<SchemaHealthReport>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize schema health response");
    }
}
