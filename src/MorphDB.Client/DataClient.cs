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
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.PagedResponseDataRecord, cancellationToken)
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
        if (await ErrorEnvelope.IsAbsentAsync(response, cancellationToken))
            return null;
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.DataRecord, cancellationToken);
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
            MorphDBJsonContext.Default.IDictionaryStringObject,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.DataRecord, cancellationToken)
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
            MorphDBJsonContext.Default.IDictionaryStringObject,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.DataRecord, cancellationToken)
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
            MorphDBJsonContext.Default.IDictionaryStringObject,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.DataRecord, cancellationToken)
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
            MorphDBJsonContext.Default.AggregateWireRequest,
            cancellationToken);
        await ErrorEnvelope.EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync(MorphDBJsonContext.Default.AggregationResponse, cancellationToken)
            ?? new AggregationResponse { Data = [] };
    }

    private static AggregateWireRequest MapToApiRequest(AggregationRequest request) => new(
        Aggregations: request.Aggregations
            .Select(a => new AggregateWireColumn(
                GetAggregateFunctionString(a.Function), a.Column, a.Alias, a.Distinct, a.Limit, a.OrderBy))
            .ToList(),
        GroupBy: request.GroupBy,
        Filter: request.Filter?
            .Select(f => new AggregateWireFilter(f.Column, GetOperatorString(f.Operator), f.Value))
            .ToList(),
        Having: request.Having?
            .Select(h => new AggregateWireHaving(h.Alias, GetOperatorString(h.Operator), h.Value))
            .ToList(),
        OrderBy: request.OrderBy?
            .Select(o => new AggregateWireOrder(o.Column, o.Descending ? "desc" : "asc"))
            .ToList(),
        Limit: request.Limit,
        Offset: request.Offset);

    private static string GetAggregateFunctionString(AggregateFunction function) => function switch
    {
        AggregateFunction.Count => "count",
        AggregateFunction.CountDistinct => "countDistinct",
        AggregateFunction.Sum => "sum",
        AggregateFunction.Avg => "avg",
        AggregateFunction.Min => "min",
        AggregateFunction.Max => "max",
        AggregateFunction.ArrayAgg => "arrayAgg",
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unsupported aggregate function.")
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
                var value = FormatFilterValue(filter.Value);
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

        if (request.Offset is { } offset)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"offset={offset}"));

        if (request.IncludeDeleted)
            parts.Add("includeDeleted=true");

        if (request.Count)
            parts.Add("count=true");

        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }

    /// <summary>
    /// A filter value as the server reads it back: numbers and instants in the invariant culture (an
    /// instant round-trips with its offset), so the query means the same whatever culture the caller
    /// runs in. A list — the values of <see cref="FilterOperator.In"/> or <see cref="FilterOperator.NotIn"/> —
    /// is written as a JSON array, the form the server reads a list in.
    /// </summary>
    internal static string FormatFilterValue(object? value) => value switch
    {
        null => string.Empty,
        System.Collections.IEnumerable list and not string => FormatFilterList(list),
        DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string FormatFilterList(System.Collections.IEnumerable list)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        // Escaped for a URL by the caller, not for HTML: the relaxed encoder keeps a value readable.
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartArray();
            foreach (var item in list)
            {
                switch (item)
                {
                    case null:
                        writer.WriteNullValue();
                        break;
                    case bool flag:
                        writer.WriteBooleanValue(flag);
                        break;
                    case byte or sbyte or short or ushort or int or uint or long or ulong:
                        writer.WriteNumberValue(Convert.ToInt64(item, CultureInfo.InvariantCulture));
                        break;
                    case float or double or decimal:
                        writer.WriteNumberValue(Convert.ToDecimal(item, CultureInfo.InvariantCulture));
                        break;
                    default:
                        writer.WriteStringValue(FormatFilterValue(item));
                        break;
                }
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static string GetOperatorString(FilterOperator op) => op switch
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
