using System.Globalization;
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
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListTableInfo, cancellationToken) ?? [];
    }

    /// <summary>
    /// Gets a table by name.
    /// </summary>
    public async Task<TableInfo?> GetTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}", cancellationToken);
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.TableInfo, cancellationToken);
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public async Task<TableInfo> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/schema/tables", request, MorphDBJsonContext.Default.CreateTableRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.TableInfo, cancellationToken)
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
            MorphDBJsonContext.Default.AddColumnRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ColumnInfo, cancellationToken)
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
            MorphDBJsonContext.Default.AlterColumnRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ColumnInfo, cancellationToken)
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
        var response = await _httpClient.PostAsJsonAsync("/api/schema/relations", request, MorphDBJsonContext.Default.CreateRelationRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.RelationInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize relation response");
    }

    /// <summary>
    /// Creates an index on a table.
    /// </summary>
    /// <remarks>
    /// Columns are named logically, as for relations; the server resolves them and refuses one the table
    /// does not have. An index is dropped with its table.
    /// </remarks>
    public async Task<IndexInfo> CreateIndexAsync(
        string tableName,
        CreateIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/schema/tables/{Uri.EscapeDataString(tableName)}/indexes", request, MorphDBJsonContext.Default.CreateIndexRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.IndexInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize index response");
    }

    /// <summary>
    /// Deletes an index.
    /// </summary>
    public async Task DeleteIndexAsync(Guid indexId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/schema/indexes/{indexId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Deletes a relation.
    /// </summary>
    public async Task DeleteRelationAsync(Guid relationId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/schema/relations/{relationId}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// The recorded changes to a table's schema, newest first — up to <paramref name="limit"/> (1 to 500;
    /// the service's default is 50).
    /// </summary>
    public async Task<IReadOnlyList<SchemaChange>> GetTableHistoryAsync(string tableName, int? limit = null, CancellationToken cancellationToken = default)
    {
        var query = limit is { } l ? string.Create(CultureInfo.InvariantCulture, $"?limit={l}") : string.Empty;
        var response = await _httpClient.GetAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}/history{query}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListSchemaChange, cancellationToken) ?? [];
    }

    /// <summary>
    /// The recorded schema changes across the project's tables, newest first — a page of
    /// <paramref name="limit"/> (1 to 500; the service's default is 50) after <paramref name="offset"/>.
    /// </summary>
    public async Task<IReadOnlyList<SchemaChange>> GetChangelogAsync(int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();
        if (limit is { } l)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"limit={l}"));
        if (offset is { } o)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"offset={o}"));
        var query = parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        var response = await _httpClient.GetAsync($"/api/schema/changelog{query}", cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.ListSchemaChange, cancellationToken) ?? [];
    }

    /// <summary>
    /// Renames a table. <see cref="UpdateTableRequest.Version"/> is the schema version last read; a table
    /// changed since answers <see cref="MorphDBConflictException"/>.
    /// </summary>
    public async Task<TableInfo> UpdateTableAsync(string tableName, UpdateTableRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}", request, MorphDBJsonContext.Default.UpdateTableRequest, cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.TableInfo, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize table response");
    }
}
