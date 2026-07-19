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
        await EnsureSuccessAsync(response, cancellationToken);
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
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TableInfo>(MorphDBJson.Options, cancellationToken);
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    public async Task<TableInfo> CreateTableAsync(CreateTableRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/schema/tables", request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<TableInfo>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize table response");
    }

    /// <summary>
    /// Drops a table.
    /// </summary>
    public async Task DropTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/schema/tables/{Uri.EscapeDataString(tableName)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
        await EnsureSuccessAsync(response, cancellationToken);
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
        await EnsureSuccessAsync(response, cancellationToken);
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
