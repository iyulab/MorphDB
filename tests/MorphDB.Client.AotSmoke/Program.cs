using System.Net;
using System.Text;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Client.Models;

// MorphDB.Client end to end under Native AOT, against a stub server: a query whose records carry
// every kind of JSON value, an insert of mixed .NET values, an aggregate, a bulk export, schema
// calls, a batch, and an error envelope. Each request body is compared to the exact text the server
// is meant to read. A path that still needs reflection throws here, so the process exits non-zero
// and the publish-and-run step fails.

var server = new StubServer();
await using var client = new MorphDBClient("http://morphdb.test", new MorphDBClientOptions
{
    ProjectId = Guid.Parse("0197c0de-0000-4000-8000-000000000001"),
    HttpMessageHandler = server,
});

// Never true in CI. It is here so the real-time connection — the hub builder, its JSON protocol and
// the payload handlers — is reachable code the AOT compiler must analyze, without a hub to connect to.
if (args is ["--realtime"])
{
    await using var subscription = await client.Realtime.SubscribeAsync("products", change => Console.WriteLine(change.RecordId));
}

// ---- query: record values arrive as .NET values ---------------------------------------------
server.Respond(HttpMethod.Get, "/api/data/products", """
    {"data":[{"id":"22222222-2222-2222-2222-222222222222",
              "data":{"lot":"L-3","qty":7,"ratio":1.5,"ok":true,"missing":null,"nested":{"a":1},"list":[1,"x"]},
              "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-02T00:00:00+00:00"}],
     "pagination":{"page":1,"pageSize":50,"totalCount":1,"totalPages":1,"hasNext":false,"hasPrevious":false}}
    """);
var page = await client.Data.QueryAsync("products", new QueryRequest { Offset = 0 });
var row = page.Data.Single().Data;
Check(Equals(row["lot"], "L-3") && Equals(row["qty"], 7L) && Equals(row["ratio"], 1.5m)
    && Equals(row["ok"], true) && row["missing"] is null, "query scalar values");
Check(row["nested"] is Dictionary<string, object?> nested && Equals(nested["a"], 1L), "query nested object");
Check(row["list"] is List<object?> { Count: 2 } list && Equals(list[1], "x"), "query nested array");

// ---- insert: mixed values written exactly as the server reads them ----------------------------
server.Respond(HttpMethod.Post, "/api/data/products", """{"id":"22222222-2222-2222-2222-222222222222","data":{"lot":"L-4"}}""");
var inserted = await client.Data.InsertAsync("products", new Dictionary<string, object?>
{
    ["lot"] = "L-4",
    ["qty"] = 3,
    ["weight"] = 2.5,
    ["price"] = 9.99m,
    ["ok"] = false,
    ["note"] = null,
    ["at"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
    ["day"] = new DateOnly(2026, 1, 2),
    ["ref"] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
    ["tags"] = new List<string> { "a", "b" },
    ["meta"] = new Dictionary<string, object?> { ["k"] = 1, ["deep"] = new object?[] { true, null } },
    ["raw"] = JsonDocument.Parse("""{"j":[1]}""").RootElement,
});
CheckBody("""
    {"lot":"L-4","qty":3,"weight":2.5,"price":9.99,"ok":false,"note":null,"at":"2026-01-02T03:04:05+00:00",
    "day":"2026-01-02","ref":"33333333-3333-3333-3333-333333333333","tags":["a","b"],
    "meta":{"k":1,"deep":[true,null]},"raw":{"j":[1]}}
    """, "insert body");
Check(Equals(inserted.Data["lot"], "L-4"), "insert response");

try
{
    await client.Data.InsertAsync("products", new Dictionary<string, object?> { ["bad"] = new Uri("http://x") });
    Check(false, "an unsupported record value is refused");
}
catch (NotSupportedException)
{
}

// ---- aggregate ----------------------------------------------------------------------------
server.Respond(HttpMethod.Post, "/api/data/products/aggregate", """
    {"data":[{"category":"a","n":3,"refs":["r1","r2"]}],"totalGroups":1,"metadata":{"rowsScanned":4,"executionTimeMs":1.5}}
    """);
var aggregate = await client.Data.AggregateAsync("products", new AggregationRequest
{
    Aggregations = [AggregationColumn.Count("n"), AggregationColumn.ArrayAgg("ref", "refs", limit: 2)],
    GroupBy = ["category"],
    Filter = [new AggregationFilter("qty", FilterOperator.GreaterThan, 1), new AggregationFilter("lot", FilterOperator.In, new List<string> { "L-1", "L-2" })],
    Having = [new HavingCondition("n", FilterOperator.GreaterThanOrEqual, 2)],
    OrderBy = [new AggregationOrderBy("category", descending: true)],
    Limit = 10,
});
CheckBody("""
    {"aggregations":[{"function":"count","column":null,"alias":"n","distinct":false,"limit":null,"orderBy":null},
    {"function":"arrayAgg","column":"ref","alias":"refs","distinct":false,"limit":2,"orderBy":null}],
    "groupBy":["category"],
    "filter":[{"column":"qty","operator":"gt","value":1},{"column":"lot","operator":"in","value":["L-1","L-2"]}],
    "having":[{"alias":"n","operator":"gte","value":2}],
    "orderBy":[{"column":"category","direction":"desc"}],"limit":10,"offset":null}
    """, "aggregate body");
var group = aggregate.Data.Single();
Check(Equals(group["n"], 3L) && group["refs"] is List<object?> { Count: 2 }, "aggregate response");
Check(aggregate.Metadata is { RowsScanned: 4 }, "aggregate metadata");

// ---- bulk ---------------------------------------------------------------------------------
server.Respond(HttpMethod.Post, "/api/bulk/products/export/csv", """
    {"jobId":"44444444-4444-4444-4444-444444444444","tableName":"products","format":"csv","status":"pending",
     "totalRows":0,"processedRows":0,"createdAt":"2026-01-01T00:00:00+00:00"}
    """);
var job = await client.Bulk.ExportCsvAsync("products", new CsvExportOptions { Columns = ["lot"], Delimiter = ';' });
CheckBody("""{"columns":["lot"],"delimiter":";","includeHeader":true,"dateFormat":null}""", "bulk export body");
Check(job.Status == "pending" && job.JobId == Guid.Parse("44444444-4444-4444-4444-444444444444"), "bulk export response");

// ---- schema -------------------------------------------------------------------------------
server.Respond(HttpMethod.Post, "/api/schema/tables", """
    {"id":"55555555-5555-5555-5555-555555555555","name":"products","version":1,
     "columns":[{"id":"66666666-6666-6666-6666-666666666666","name":"lot","type":"text","nullable":false,"position":1}],
     "createdAt":"2026-01-01T00:00:00+00:00","updatedAt":"2026-01-01T00:00:00+00:00"}
    """);
var table = await client.Schema.CreateTableAsync(new CreateTableRequest
{
    Name = "products",
    Columns = [new CreateColumnRequest { Name = "lot", Type = "text", Nullable = false }],
});
CheckBody("""
    {"name":"products","columns":[{"name":"lot","type":"text","nullable":false,"unique":false,"indexed":false,
    "default":null,"check":null}],"systemColumns":null}
    """, "create table body");
Check(table.Columns is [{ Name: "lot", Nullable: false }], "create table response");

server.Respond(HttpMethod.Get, "/api/schema/tables", """[{"id":"55555555-5555-5555-5555-555555555555","name":"products","version":1,"columns":[]}]""");
var tables = await client.Schema.GetTablesAsync();
Check(tables is [{ Name: "products" }], "list tables");

// ---- batch --------------------------------------------------------------------------------
server.Respond(HttpMethod.Post, "/api/batch/data/products/insert", """
    {"results":[{"index":0,"success":true,"affectedRows":1,"data":{"_id":"77777777-7777-7777-7777-777777777777"}}],
     "successCount":1,"failureCount":0}
    """);
var batch = await client.Batch.InsertManyAsync("products", [new Dictionary<string, object?> { ["lot"] = "L-5" }]);
CheckBody("""[{"lot":"L-5"}]""", "batch insert body");
Check(batch.SuccessCount == 1 && Equals(batch.Results[0].Data?["_id"], "77777777-7777-7777-7777-777777777777"), "batch response");

// ---- error envelope -----------------------------------------------------------------------
server.Respond(HttpMethod.Post, "/api/schema/tables", """{"error":"Bad Request","message":"Name is taken.","code":"DUPLICATE"}""", HttpStatusCode.BadRequest);
try
{
    await client.Schema.CreateTableAsync(new CreateTableRequest { Name = "products" });
    Check(false, "a validation error is raised");
}
catch (MorphDBValidationException error)
{
    Check(error.Message == "Name is taken." && error.ErrorCode == "DUPLICATE", "error envelope");
}

Console.WriteLine("MorphDB.Client AOT smoke: ok");
return 0;

void CheckBody(string expected, string what)
{
    var flat = expected.ReplaceLineEndings(string.Empty);
    Check(server.LastBody == flat, $"{what}: expected {flat}, got {server.LastBody}");
}

static void Check(bool condition, string what)
{
    if (!condition)
    {
        throw new InvalidOperationException($"AOT smoke check failed: {what}");
    }
}

/// <summary>Answers each route with the JSON it was given, and remembers the last request body.</summary>
internal sealed class StubServer : HttpMessageHandler
{
    private readonly Dictionary<(HttpMethod, string), (string Json, HttpStatusCode Status)> _routes = [];

    public string? LastBody { get; private set; }

    public void Respond(HttpMethod method, string path, string json, HttpStatusCode status = HttpStatusCode.OK)
        => _routes[(method, path)] = (json, status);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (!_routes.TryGetValue((request.Method, request.RequestUri!.AbsolutePath), out var route))
        {
            throw new InvalidOperationException($"AOT smoke: no stub for {request.Method} {request.RequestUri}");
        }

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Json, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
