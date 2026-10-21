using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        var response = request?.Where is null
            ? await _httpClient.GetAsync(
                $"/api/data/{Uri.EscapeDataString(tableName)}{BuildQueryString(request)}",
                cancellationToken)
            : await _httpClient.PostAsJsonAsync(
                $"/api/data/{Uri.EscapeDataString(tableName)}/query",
                QueryBody(request),
                MorphDBJsonContext.Default.QueryWireRequest,
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
    /// Inserts a record, or updates the one whose key columns hold the same values — columns a unique
    /// constraint covers together. A record without an <c>_id</c> is assigned one when it is inserted.
    /// </summary>
    public async Task<DataRecord> UpsertAsync(
        string tableName,
        IDictionary<string, object?> data,
        IReadOnlyList<string> keyColumns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(keyColumns);
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/batch/data/{Uri.EscapeDataString(tableName)}",
            new UpsertWireRequest(data, keyColumns),
            MorphDBJsonContext.Default.UpsertWireRequest,
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

    /// <summary>The query as a body: <see cref="QueryRequest.Filters"/> and the tree under one <c>and</c>.</summary>
    private static QueryWireRequest QueryBody(QueryRequest request)
    {
        var tree = Wire(request.Where!);
        var filter = request.Filters.Count == 0
            ? tree
            : new QueryWireGroup("and", [.. request.Filters.Select(f => Wire(f)), tree]);
        return new QueryWireRequest(
            filter,
            request.Select.Count > 0 ? request.Select : null,
            request.OrderBy.Count > 0 ? [.. request.OrderBy.Select(o => $"{o.Column}:{(o.Ascending ? "asc" : "desc")}")] : null,
            request.Page,
            request.PageSize,
            request.Offset,
            request.IncludeDeleted,
            request.Count);
    }

    private static QueryWireNode Wire(FilterNode node) => node switch
    {
        FilterCondition condition => new QueryWireCondition(condition.Filter.Column, GetOperatorString(condition.Filter.Operator), condition.Filter.Value),
        FilterGroup group => new QueryWireGroup(group.Logic == FilterLogic.Or ? "or" : "and", [.. group.Filters.Select(Wire)]),
        _ => throw new ArgumentException($"A filter node of type {node.GetType().Name} cannot be sent.", nameof(node)),
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
                parts.Add($"filter={Uri.EscapeDataString(FilterText(filter))}");
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

    /// <summary>One condition as the service reads it in a query string or a filter list: <c>column:operator:value</c>.</summary>
    internal static string FilterText(Filter filter) =>
        $"{filter.Column}:{GetOperatorString(filter.Operator)}:{FormatFilterValue(filter.Value)}";

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

/// <summary>The body of <c>PUT /api/batch/data/{table}</c>.</summary>
internal sealed record UpsertWireRequest(IDictionary<string, object?> Data, IReadOnlyList<string> KeyColumns);

/// <summary>The body of <c>POST /api/data/{table}/query</c>.</summary>
internal sealed record QueryWireRequest(
    QueryWireNode? Filter,
    IReadOnlyList<string>? Select,
    IReadOnlyList<string>? OrderBy,
    int Page,
    int PageSize,
    int? Offset,
    bool IncludeDeleted,
    bool Count);

/// <summary>A filter tree node as the service reads it, named by <c>$type</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(QueryWireCondition), "condition")]
[JsonDerivedType(typeof(QueryWireGroup), "group")]
internal abstract record QueryWireNode;

/// <summary>One condition, the operator named as the service spells it.</summary>
internal sealed record QueryWireCondition(string Column, string Operator, object? Value) : QueryWireNode;

/// <summary>A group, <c>and</c> or <c>or</c>.</summary>
internal sealed record QueryWireGroup(string Logic, IReadOnlyList<QueryWireNode> Filters) : QueryWireNode;
