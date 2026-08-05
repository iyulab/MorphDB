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
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<PagedResponse<DataRecord>>(MorphDBJson.Options, cancellationToken)
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
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(MorphDBJson.Options, cancellationToken);
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
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(MorphDBJson.Options, cancellationToken)
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
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(MorphDBJson.Options, cancellationToken)
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
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
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
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DataRecord>(MorphDBJson.Options, cancellationToken)
            ?? throw new MorphDBException("Failed to deserialize record response");
    }

    /// <summary>
    /// Performs aggregation on a table with optional grouping.
    /// </summary>
    /// <param name="tableName">Name of the table.</param>
    /// <param name="request">Aggregation request with functions, grouping, filters, and ordering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregation response with data and metadata.</returns>
    public async Task<AggregationResponse> AggregateAsync(
        string tableName,
        AggregationRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiRequest = MapToApiRequest(request);
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/data/{Uri.EscapeDataString(tableName)}/aggregate",
            apiRequest,
            MorphDBJson.Options,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<AggregationResponse>(MorphDBJson.Options, cancellationToken)
            ?? new AggregationResponse { Data = [] };
    }

    private static object MapToApiRequest(AggregationRequest request)
    {
        return new
        {
            aggregations = request.Aggregations.Select(a => new
            {
                function = GetAggregateFunctionString(a.Function),
                column = a.Column,
                alias = a.Alias,
                distinct = a.Distinct
            }).ToList(),
            groupBy = request.GroupBy,
            filter = request.Filter?.Select(f => new
            {
                column = f.Column,
                @operator = GetOperatorString(f.Operator),
                value = f.Value
            }).ToList(),
            having = request.Having?.Select(h => new
            {
                alias = h.Alias,
                @operator = GetOperatorString(h.Operator),
                value = h.Value
            }).ToList(),
            orderBy = request.OrderBy?.Select(o => new
            {
                column = o.Column,
                descending = o.Descending
            }).ToList(),
            limit = request.Limit,
            offset = request.Offset
        };
    }

    private static string GetAggregateFunctionString(AggregateFunction function) => function switch
    {
        AggregateFunction.Count => "count",
        AggregateFunction.CountDistinct => "countDistinct",
        AggregateFunction.Sum => "sum",
        AggregateFunction.Avg => "avg",
        AggregateFunction.Min => "min",
        AggregateFunction.Max => "max",
        _ => "count"
    };

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

}
