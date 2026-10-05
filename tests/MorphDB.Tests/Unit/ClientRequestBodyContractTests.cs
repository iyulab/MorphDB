using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MorphDB.Client;
using MorphDB.Client.Models;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Pins the exact bytes the client puts on the wire, so the serializer underneath it can change
/// without the requests changing.
/// <para>
/// The client's request bodies were once written by reflection over whatever runtime type a value
/// had — including an anonymous object for the aggregate request. That is what kept the client from
/// trimming or compiling ahead of time, and replacing it touches every body the client sends. The
/// reference each test compares against is the reflection serializer itself (Web defaults, no
/// converters) over the same values or, for the aggregate, over the anonymous shape the client used
/// to send — so what is checked is "the same as before", not merely "something the server accepts".
/// Literal strings sit beside the references where the format is the point, so the pin does not
/// rest on the reference serializer alone.
/// </para>
/// </summary>
public class ClientRequestBodyContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Captures the request a client method issues, without a server.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static MorphDBClient ClientOver(CapturingHandler handler)
        => new("http://morphdb.test", new MorphDBClientOptions { HttpMessageHandler = handler });

    /// <summary>A JSON text written over several lines for reading, as the one line it is on the wire.</summary>
    private static string Joined(string lines) => lines.ReplaceLineEndings(string.Empty);

    private const string RecordJson = """{"id":"22222222-2222-2222-2222-222222222222","data":{}}""";

    // ---- record values -----------------------------------------------------------------------

    /// <summary>One of every kind of value a record may carry, keyed by what it is.</summary>
    private static Dictionary<string, object?> EveryValueKind() => new()
    {
        ["null"] = null,
        ["string"] = "a<b&'\"é",
        ["char"] = 'x',
        ["true"] = true,
        ["false"] = false,
        ["sbyte"] = (sbyte)-8,
        ["byte"] = (byte)8,
        ["short"] = (short)-16,
        ["ushort"] = (ushort)16,
        ["int"] = -32,
        ["uint"] = 32u,
        ["long"] = long.MinValue,
        ["ulong"] = ulong.MaxValue,
        ["int128"] = Int128.MaxValue,
        ["uint128"] = UInt128.MaxValue,
        ["half"] = (Half)0.1,
        ["float"] = float.MaxValue,
        ["double"] = 0.1,
        ["doubleExponent"] = 1e20,
        ["decimal"] = 3.14m,
        ["enum"] = DayOfWeek.Friday,
        ["ulongEnum"] = WideFlags.Top,
        ["dateTimeUtc"] = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        ["dateTimeLocalTicks"] = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1234567),
        ["dateTimeOffset"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)),
        ["dateOnly"] = new DateOnly(2026, 1, 2),
        ["timeOnly"] = new TimeOnly(3, 4, 5),
        ["timeOnlyFraction"] = new TimeOnly(3, 4, 5, 123),
        ["timeSpan"] = TimeSpan.FromMinutes(90.5),
        ["timeSpanNegativeDays"] = TimeSpan.FromDays(-1.25),
        ["timeSpanFraction"] = new TimeSpan(1, 2, 3, 4, 5),
        ["guid"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ["bytes"] = new byte[] { 1, 2, 3 },
        ["jsonElement"] = JsonDocument.Parse("""{"a":[1,"two",null]}""").RootElement,
        ["jsonNode"] = JsonNode.Parse("""{"z":1}"""),
        ["jsonArrayNode"] = new JsonArray(1, 2),
        ["dictionary"] = new Dictionary<string, object?> { ["n"] = 1, ["inner"] = new Dictionary<string, object?> { ["deep"] = null } },
        ["readOnlyDictionary"] = new ReadOnlyOnly(new Dictionary<string, object?> { ["r"] = "o" }),
        ["typedDictionary"] = new Dictionary<string, int> { ["b"] = 2 },
        ["hashtable"] = new Hashtable { ["h"] = 1 },
        ["list"] = new List<object?> { 1L, "two", null, new List<object?> { true } },
        ["intArray"] = new[] { 3, 4 },
        ["stringList"] = new List<string> { "x", "y" },
    };

    /// <summary>An enum whose values do not fit a signed 64-bit integer.</summary>
    private enum WideFlags : ulong
    {
        Top = ulong.MaxValue,
    }

    /// <summary>
    /// A dictionary that is only an <see cref="IReadOnlyDictionary{TKey, TValue}"/> — no mutable
    /// dictionary interface — so that branch is exercised on its own.
    /// </summary>
    private sealed class ReadOnlyOnly(Dictionary<string, object?> inner) : IReadOnlyDictionary<string, object?>
    {
        public object? this[string key] => inner[key];
        public IEnumerable<string> Keys => inner.Keys;
        public IEnumerable<object?> Values => inner.Values;
        public int Count => inner.Count;
        public bool ContainsKey(string key) => inner.ContainsKey(key);
        public bool TryGetValue(string key, out object? value) => inner.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => inner.GetEnumerator();
    }

    [Fact]
    public async Task Every_kind_of_record_value_is_written_as_the_reflection_serializer_wrote_it()
    {
        var handler = new CapturingHandler(RecordJson);
        var values = EveryValueKind();

        await ClientOver(handler).Data.InsertAsync("t", values, TestContext.Current.CancellationToken);

        handler.Body.Should().Be(JsonSerializer.Serialize<IDictionary<string, object?>>(values, WebOptions));
    }

    [Fact]
    public async Task Dates_times_and_binary_keep_their_literal_formats()
    {
        var handler = new CapturingHandler(RecordJson);
        var all = EveryValueKind();
        var keys = new[]
        {
            "dateTimeUtc", "dateTimeLocalTicks", "dateTimeOffset", "dateOnly", "timeOnly", "timeOnlyFraction",
            "timeSpan", "timeSpanNegativeDays", "timeSpanFraction", "guid", "bytes", "enum", "ulongEnum",
        };
        var values = keys.ToDictionary(k => k, k => all[k]);

        await ClientOver(handler).Data.InsertAsync("t", values, TestContext.Current.CancellationToken);

        handler.Body.Should().Be(Joined(
            """
            {"dateTimeUtc":"2026-01-02T03:04:05Z","dateTimeLocalTicks":"2026-01-02T03:04:05.1234567",
            "dateTimeOffset":"2026-01-02T03:04:05+09:00","dateOnly":"2026-01-02","timeOnly":"03:04:05",
            "timeOnlyFraction":"03:04:05.1230000","timeSpan":"01:30:30","timeSpanNegativeDays":"-1.06:00:00",
            "timeSpanFraction":"1.02:03:04.0050000","guid":"11111111-2222-3333-4444-555555555555",
            "bytes":"AQID","enum":5,"ulongEnum":18446744073709551615}
            """));
    }

    [Fact]
    public async Task Update_upsert_and_batch_insert_write_record_values_the_same_way()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var values = EveryValueKind();
        var expected = JsonSerializer.Serialize<IDictionary<string, object?>>(values, WebOptions);

        var update = new CapturingHandler(RecordJson);
        await ClientOver(update).Data.UpdateAsync("t", Guid.Empty, values, cancellationToken);
        update.Body.Should().Be(expected);

        var upsert = new CapturingHandler(RecordJson);
        await ClientOver(upsert).Data.UpsertAsync("t", values, cancellationToken);
        upsert.Body.Should().Be(expected);

        var batch = new CapturingHandler("""{"results":[],"successCount":0,"failureCount":0}""");
        await ClientOver(batch).Batch.InsertManyAsync("t", [values, values], cancellationToken);
        batch.Body.Should().Be($"[{expected},{expected}]");
    }

    [Fact]
    public async Task A_record_value_of_an_arbitrary_type_is_refused_rather_than_serialized_by_reflection()
    {
        var handler = new CapturingHandler(RecordJson);
        var values = new Dictionary<string, object?> { ["point"] = new { X = 1, Y = 2 } };

        var act = () => ClientOver(handler).Data.InsertAsync("t", values, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("<>f__AnonymousType").And.Contain("JsonElement");
    }

    [Fact]
    public async Task A_dictionary_value_with_non_string_keys_is_refused()
    {
        var handler = new CapturingHandler(RecordJson);
        var values = new Dictionary<string, object?> { ["map"] = new Hashtable { [1] = "one" } };

        var act = () => ClientOver(handler).Data.InsertAsync("t", values, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ---- aggregate request -------------------------------------------------------------------

    /// <summary>
    /// The aggregate request exactly as the client built it before it had a type of its own: an
    /// anonymous object serialized by reflection. Kept verbatim as the reference.
    /// </summary>
    private static object LegacyAggregateBody(AggregationRequest request) => new
    {
        aggregations = request.Aggregations.Select(a => new
        {
            function = a.Function switch
            {
                AggregateFunction.Count => "count",
                AggregateFunction.CountDistinct => "countDistinct",
                AggregateFunction.Sum => "sum",
                AggregateFunction.Avg => "avg",
                AggregateFunction.Min => "min",
                AggregateFunction.Max => "max",
                AggregateFunction.ArrayAgg => "arrayAgg",
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            },
            column = a.Column,
            alias = a.Alias,
            distinct = a.Distinct,
            limit = a.Limit,
            orderBy = a.OrderBy,
        }).ToList(),
        groupBy = request.GroupBy,
        filter = request.Filter?.Select(f => new
        {
            column = f.Column,
            @operator = LegacyOperator(f.Operator),
            value = f.Value,
        }).ToList(),
        having = request.Having?.Select(h => new
        {
            alias = h.Alias,
            @operator = LegacyOperator(h.Operator),
            value = h.Value,
        }).ToList(),
        orderBy = request.OrderBy?.Select(o => new
        {
            column = o.Column,
            direction = o.Descending ? "desc" : "asc",
        }).ToList(),
        limit = request.Limit,
        offset = request.Offset,
    };

    private static string LegacyOperator(FilterOperator op) => op switch
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
        _ => "eq",
    };

    private static AggregationRequest FullAggregate() => new()
    {
        Aggregations =
        [
            AggregationColumn.Count("n"),
            new AggregationColumn { Function = AggregateFunction.Sum, Column = "qty", Alias = "total", Distinct = true },
            AggregationColumn.ArrayAgg("ref", "refs", limit: 2),
        ],
        GroupBy = ["category", "lot"],
        Filter =
        [
            new AggregationFilter("status", FilterOperator.Equal, "open"),
            new AggregationFilter("qty", FilterOperator.GreaterThan, 3),
            new AggregationFilter("lot", FilterOperator.In, new List<string> { "L-1", "L-2" }),
            new AggregationFilter("deleted", FilterOperator.IsNull, null),
            new AggregationFilter("at", FilterOperator.LessThan, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
        ],
        Having = [new HavingCondition("n", FilterOperator.GreaterThanOrEqual, 2L)],
        OrderBy = [new AggregationOrderBy("category", descending: true), new AggregationOrderBy("lot")],
        Limit = 10,
        Offset = 5,
    };

    [Fact]
    public async Task A_full_aggregate_request_is_the_body_the_client_always_sent()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        var request = FullAggregate();

        await ClientOver(handler).Data.AggregateAsync("t", request, TestContext.Current.CancellationToken);

        handler.Body.Should().Be(JsonSerializer.Serialize(LegacyAggregateBody(request), WebOptions));
        handler.Body.Should().Be(Joined(
            """
            {"aggregations":[{"function":"count","column":null,"alias":"n","distinct":false,"limit":null,"orderBy":null},
            {"function":"sum","column":"qty","alias":"total","distinct":true,"limit":null,"orderBy":null},
            {"function":"arrayAgg","column":"ref","alias":"refs","distinct":false,"limit":2,"orderBy":null}],
            "groupBy":["category","lot"],
            "filter":[{"column":"status","operator":"eq","value":"open"},{"column":"qty","operator":"gt","value":3},
            {"column":"lot","operator":"in","value":["L-1","L-2"]},{"column":"deleted","operator":"isnull","value":null},
            {"column":"at","operator":"lt","value":"2026-01-02T00:00:00+00:00"}],
            "having":[{"alias":"n","operator":"gte","value":2}],
            "orderBy":[{"column":"category","direction":"desc"},{"column":"lot","direction":"asc"}],
            "limit":10,"offset":5}
            """));
    }

    [Fact]
    public async Task A_minimal_aggregate_request_still_writes_its_absent_parts_as_null()
    {
        var handler = new CapturingHandler("""{"data":[]}""");
        var request = new AggregationRequest { Aggregations = [AggregationColumn.Count()] };

        await ClientOver(handler).Data.AggregateAsync("t", request, TestContext.Current.CancellationToken);

        handler.Body.Should().Be(JsonSerializer.Serialize(LegacyAggregateBody(request), WebOptions));
        handler.Body.Should().Be(Joined(
            """
            {"aggregations":[{"function":"count","column":null,"alias":"count","distinct":false,"limit":null,"orderBy":null}],
            "groupBy":[],"filter":null,"having":null,"orderBy":null,"limit":null,"offset":null}
            """));
    }

    // ---- typed request models ----------------------------------------------------------------

    [Fact]
    public async Task Every_typed_request_body_is_what_the_reflection_serializer_wrote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var record = new Dictionary<string, object?> { ["name"] = "widget", ["qty"] = 3, ["at"] = new DateOnly(2026, 1, 2) };
        var settings = new ProjectSettings { Timezone = "UTC", Metadata = new Dictionary<string, string> { ["k"] = "v" } };

        var cases = new (string Name, object Model, string Response, Func<MorphDBClient, object, Task> Send)[]
        {
            ("batch", new BatchRequest
            {
                Operations =
                [
                    new BatchOperation { Method = BatchMethod.Insert, Table = "t", Data = record },
                    new BatchOperation { Method = BatchMethod.Delete, Table = "t", Id = Guid.Parse("11111111-1111-1111-1111-111111111111") },
                ],
            }, """{"results":[],"successCount":0,"failureCount":0}""",
                (c, m) => c.Batch.ExecuteAsync((BatchRequest)m, cancellationToken)),
            ("transaction", new TransactionRequest
            {
                Operations = [new TransactionOperation { Ref = "a", Table = "t", Operation = "insert", Data = record }],
            }, """{"success":true,"results":[]}""",
                (c, m) => c.Transactions.ExecuteAsync((TransactionRequest)m, cancellationToken)),
            ("finalize", new FinalizeRequest { RecordIds = [Guid.Parse("11111111-1111-1111-1111-111111111111")] },
                """{"success":true,"finalizedCount":1,"errorCount":0,"errors":[]}""",
                (c, m) => c.Transactions.FinalizeBatchAsync("t", (FinalizeRequest)m, cancellationToken)),
            ("createTable", new CreateTableRequest
            {
                Name = "t",
                Columns = [new CreateColumnRequest { Name = "c", Type = "text", Check = "c <> ''" }],
                SystemColumns = new SystemColumnOptions { SoftDelete = true },
            }, """{"id":"11111111-1111-1111-1111-111111111111","name":"t","version":1,"columns":[]}""",
                (c, m) => c.Schema.CreateTableAsync((CreateTableRequest)m, cancellationToken)),
            ("addColumn", new AddColumnRequest { Name = "c", Type = "integer", Default = "0" },
                """{"name":"c","type":"integer"}""",
                (c, m) => c.Schema.AddColumnAsync("t", (AddColumnRequest)m, cancellationToken)),
            ("alterColumn", new AlterColumnRequest { Type = "bigint", Nullable = false, Version = 2, ForceCast = true },
                """{"name":"c","type":"bigint"}""",
                (c, m) => c.Schema.AlterColumnAsync("t", "c", (AlterColumnRequest)m, cancellationToken)),
            ("createRelation", new CreateRelationRequest
            {
                Name = "r", SourceTable = "a", SourceColumn = "b_id", TargetTable = "b", TargetColumn = "id", EnforceOnWrite = false,
            }, """{"id":"11111111-1111-1111-1111-111111111111","name":"r","type":"one-to-many","onDelete":"no-action"}""",
                (c, m) => c.Schema.CreateRelationAsync((CreateRelationRequest)m, cancellationToken)),
            ("createProject", new CreateProjectRequest { Name = "P", Slug = "p", Settings = settings },
                """{"id":"11111111-1111-1111-1111-111111111111","name":"P","slug":"p","systemSchema":"s","dataSchema":"d","status":"active"}""",
                (c, m) => c.Projects.CreateAsync((CreateProjectRequest)m, cancellationToken)),
            ("updateProject", new UpdateProjectRequest { Settings = settings },
                """{"id":"11111111-1111-1111-1111-111111111111","name":"P","slug":"p","systemSchema":"s","dataSchema":"d","status":"active"}""",
                (c, m) => c.Projects.UpdateAsync(Guid.Empty, (UpdateProjectRequest)m, cancellationToken)),
            ("createView", new CreateViewRequest { Name = "v", BaseTable = "t" },
                """{"name":"v","baseTable":"t","columns":[]}""",
                (c, m) => c.Views.CreateAsync((CreateViewRequest)m, cancellationToken)),
            ("createWebhook", new CreateWebhookRequest
            {
                Name = "w", TableName = "t", Url = "https://example.test/hook", Headers = new Dictionary<string, string> { ["x"] = "y" },
            }, """{"webhookId":"11111111-1111-1111-1111-111111111111","name":"w","tableName":"t","url":"https://example.test/hook"}""",
                (c, m) => c.Webhooks.CreateAsync((CreateWebhookRequest)m, cancellationToken)),
            ("exportCsv", new CsvExportOptions { Columns = ["a"], Delimiter = ';' },
                """{"jobId":"11111111-1111-1111-1111-111111111111","tableName":"t","format":"csv","status":"pending"}""",
                (c, m) => c.Bulk.ExportCsvAsync("t", (CsvExportOptions)m, cancellationToken)),
            ("exportJson", new JsonExportOptions { Pretty = true },
                """{"jobId":"11111111-1111-1111-1111-111111111111","tableName":"t","format":"json","status":"pending"}""",
                (c, m) => c.Bulk.ExportJsonAsync("t", (JsonExportOptions)m, cancellationToken)),
            ("exportXlsx", new XlsxExportOptions { SheetName = "S" },
                """{"jobId":"11111111-1111-1111-1111-111111111111","tableName":"t","format":"xlsx","status":"pending"}""",
                (c, m) => c.Bulk.ExportXlsxAsync("t", (XlsxExportOptions)m, cancellationToken)),
        };

        foreach (var (name, model, response, send) in cases)
        {
            var handler = new CapturingHandler(response);

            await send(ClientOver(handler), model);

            handler.Body.Should().Be(JsonSerializer.Serialize(model, model.GetType(), WebOptions), name);
        }
    }
}
