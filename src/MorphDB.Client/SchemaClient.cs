using System.Net.Http.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for schema management operations.
/// </summary>
public sealed class SchemaClient
{
    private readonly HttpClient _httpClient;

    internal SchemaClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets all tables.
    /// </summary>
    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync("/api/schema/tables", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<TableInfo>>(MorphDBJson.Options, cancellationToken) ?? [];
    }

    /// <summary>
    /// Gets a table by name.
    /// </summary>
    public async Task<TableInfo?> GetTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TableInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public async Task<TableInfo> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/schema/tables", request, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TableInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize table response");
    }

    /// <summary>
    /// Drops a table.
    /// </summary>
    public async Task DropTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Adds a column to an existing table.
    /// </summary>
    public async Task<ColumnInfo> AddColumnAsync(string tableName, AddColumnRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/schema/tables/{Uri.EscapeDataString(tableName)}/columns",
            request,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ColumnInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize column response");
    }

    /// <summary>
    /// Alters a column.
    /// </summary>
    public async Task<ColumnInfo> AlterColumnAsync(string tableName, string columnName, AlterColumnRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"/api/schema/tables/{Uri.EscapeDataString(tableName)}/columns/{Uri.EscapeDataString(columnName)}",
            request,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<ColumnInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize column response");
    }

    /// <summary>
    /// Drops a column from a table.
    /// </summary>
    public async Task DropColumnAsync(string tableName, string columnName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/schema/tables/{Uri.EscapeDataString(tableName)}/columns/{Uri.EscapeDataString(columnName)}",
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Creates a relation between two tables.
    /// </summary>
    /// <remarks>
    /// Tables and columns are named logically; the server resolves them, so a caller never handles
    /// internal identifiers. Set <see cref="CreateRelationRequest.EnforceOnWrite"/> to false to
    /// declare the link without gating writes on it — what a caller that rebuilds its tables
    /// wholesale needs, since a child can be written before its parent has been reloaded.
    /// </remarks>
    public async Task<RelationInfo> CreateRelationAsync(
        CreateRelationRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/schema/relations", request, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<RelationInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize relation response");
    }

    /// <summary>
    /// Deletes a relation.
    /// </summary>
    public async Task DeleteRelationAsync(Guid relationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/schema/relations/{relationId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }
}
