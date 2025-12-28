using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Client.Models;

namespace MorphDB.Client;

/// <summary>
/// Client for data operations.
/// </summary>
public sealed class DataClient
{
    private readonly HttpClient _httpClient;

    internal DataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Queries records from a table.
    /// </summary>
    public async Task<PagedResponse<DataRecord>> QueryAsync(
        string tableName,
        QueryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var queryString = BuildQueryString(request);
        var response = await _httpClient.GetAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}{queryString}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PagedResponse<DataRecord>>(cancellationToken: cancellationToken)
            ?? new PagedResponse<DataRecord> { Data = [], Pagination = new PaginationInfo() };
    }

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    public async Task<DataRecord?> GetByIdAsync(
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/{id}",
            cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Inserts a new record.
    /// </summary>
    public async Task<DataRecord> InsertAsync(
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}",
            data,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(cancellationToken: cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize record response");
    }

    /// <summary>
    /// Updates an existing record.
    /// </summary>
    public async Task<DataRecord> UpdateAsync(
        string tableName,
        Guid id,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PatchAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/{id}",
            data,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(cancellationToken: cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize record response");
    }

    /// <summary>
    /// Deletes a record.
    /// </summary>
    public async Task DeleteAsync(
        string tableName,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/{id}",
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Performs batch operations.
    /// </summary>
    public async Task<BatchResponse> BatchAsync(
        string tableName,
        BatchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/batch",
            request,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BatchResponse>(cancellationToken: cancellationToken)
            ?? new BatchResponse();
    }

    /// <summary>
    /// Upserts a record (insert or update based on ID).
    /// </summary>
    public async Task<DataRecord> UpsertAsync(
        string tableName,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}",
            data,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(cancellationToken: cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize record response");
    }

    private static string BuildQueryString(QueryRequest? request)
    {
        if (request == null)
            return string.Empty;

        var parts = new List<string>();

        if (request.Select.Count > 0)
            parts.Add($"select={Uri.EscapeDataString(string.Join(",", request.Select))}");

        if (request.Filters.Count > 0)
        {
            foreach (var filter in request.Filters)
            {
                var op = GetOperatorString(filter.Operator);
                var value = filter.Value?.ToString() ?? "";
                parts.Add($"filter={Uri.EscapeDataString($"{filter.Column}:{op}:{value}")}");
            }
        }

        if (request.OrderBy.Count > 0)
        {
            var orderParts = request.OrderBy.Select(o => $"{o.Column}:{(o.Ascending ? "asc" : "desc")}");
            parts.Add($"orderBy={Uri.EscapeDataString(string.Join(",", orderParts))}");
        }

        if (request.Page > 1)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"page={request.Page}"));

        if (request.PageSize != 50)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"pageSize={request.PageSize}"));

        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }

    private static string GetOperatorString(FilterOperator op) => op switch
    {
        FilterOperator.Equal => "eq",
        FilterOperator.NotEqual => "neq",
        FilterOperator.GreaterThan => "gt",
        FilterOperator.GreaterThanOrEqual => "gte",
        FilterOperator.LessThan => "lt",
        FilterOperator.LessThanOrEqual => "lte",
        FilterOperator.Contains => "contains",
        FilterOperator.StartsWith => "startswith",
        FilterOperator.EndsWith => "endswith",
        FilterOperator.IsNull => "isnull",
        FilterOperator.IsNotNull => "isnotnull",
        FilterOperator.In => "in",
        FilterOperator.NotIn => "notin",
        _ => "eq"
    };

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
