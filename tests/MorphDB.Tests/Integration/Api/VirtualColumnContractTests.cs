using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A declared column is one column whichever kind it is: a lookup, a rollup or a formula reads,
/// filters, orders, groups and aggregates through every read route exactly as a stored column does.
/// <para>
/// Before, those columns were computed only where a SELECT list named them. A list read carried
/// lookups and rollups, but a filter, an order, a grouping or an aggregate on any of them answered
/// <c>500</c> (the clause named a physical column that does not exist), a single-record read left
/// them out, and a formula never produced a value at all — its syntax tree lost every child on the
/// way to the translator, which then emitted NULL without a word. A table whose only derived column
/// was a formula could not be listed.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class VirtualColumnContractTests
{
    private readonly HttpClient _client;

    public VirtualColumnContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private sealed record Shop(string Customers, string Orders, string Products, string C1, string O1, string P1);

    /// <summary>
    /// customers(code, grade) + rollup order_total = SUM(orders.amount);
    /// orders(customer_id, amount) + lookup customer_grade = customers.grade + formula doubled = amount * 2;
    /// products(price) + formula doubled = price * 2 — a table whose only derived column is a formula.
    /// </summary>
    private async Task<Shop> SetupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var customers = $"vc_cust_{s}";
        var orders = $"vc_ord_{s}";
        var products = $"vc_prod_{s}";

        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customers,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text" }, new CreateColumnApiRequest { Name = "grade", Type = "text" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_id", Type = "uuid" },
                new CreateColumnApiRequest { Name = "amount", Type = "integer" },
                new CreateColumnApiRequest
                {
                    Name = "customer_grade", Type = "text",
                    Lookup = new LookupConfigApiRequest { RelationColumn = "customer_id", TargetTable = customers, TargetColumn = "grade" },
                },
                new CreateColumnApiRequest { Name = "doubled", Type = "integer", Formula = new FormulaConfigApiRequest { Formula = "{amount} * 2", ReturnType = "integer" } },
            ],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync($"/api/schema/tables/{customers}/columns", new AddColumnApiRequest
        {
            Name = "order_total",
            Type = "integer",
            Rollup = new RollupConfigApiRequest { Relation = orders, TargetTable = orders, ForeignKeyColumn = "customer_id", SourceColumn = "amount", Aggregation = "sum" },
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = products,
            Columns =
            [
                new CreateColumnApiRequest { Name = "price", Type = "integer" },
                new CreateColumnApiRequest { Name = "doubled", Type = "integer", Formula = new FormulaConfigApiRequest { Formula = "{price} * 2", ReturnType = "integer" } },
            ],
        }, ct));

        var c1 = await InsertAsync(customers, new { code = "C-1", grade = "A" });
        var c2 = await InsertAsync(customers, new { code = "C-2", grade = "B" });
        var o1 = await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c1, ["amount"] = 5 });
        await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c1, ["amount"] = 7 });
        await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c2, ["amount"] = 1 });
        var p1 = await InsertAsync(products, new { price = 5 });
        await InsertAsync(products, new { price = 9 });

        return new Shop(customers, orders, products, c1, o1, p1);
    }

    [Fact]
    public async Task Each_kind_of_derived_column_carries_its_value_in_a_list_and_in_a_single_record()
    {
        var shop = await SetupAsync();

        (await ListValuesAsync(shop.Orders, "customer_grade", "amount")).Should().Equal("B", "A", "A");
        (await ListValuesAsync(shop.Orders, "doubled", "amount")).Should().Equal("2", "10", "14");
        (await ListValuesAsync(shop.Customers, "order_total", "code")).Should().Equal("12", "1");
        (await ListValuesAsync(shop.Products, "doubled", "price")).Should().Equal(
            ["10", "18"], "a table whose only derived column is a formula is read like any other");

        var order = await GetDataAsync($"/api/data/{shop.Orders}/{shop.O1}");
        order.GetProperty("customer_grade").GetString().Should().Be("A");
        order.GetProperty("doubled").GetInt32().Should().Be(10);
        (await GetDataAsync($"/api/data/{shop.Customers}/{shop.C1}")).GetProperty("order_total").GetInt64().Should().Be(12);
        (await GetDataAsync($"/api/data/{shop.Products}/{shop.P1}")).GetProperty("doubled").GetInt32().Should().Be(10);
    }

    [Theory]
    [InlineData("orders", "customer_grade", "A", 2)]
    [InlineData("orders", "doubled", "10", 1)]
    [InlineData("customers", "order_total", "12", 1)]
    [InlineData("products", "doubled", "18", 1)]
    public async Task A_derived_column_filters_through_every_query_route(string which, string column, string value, int expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SetupAsync();
        var table = Table(shop, which);
        var json = int.TryParse(value, out _) ? value : $"\"{value}\"";

        (await CountRowsAsync($"/api/data/{table}?filter={column}:eq:{value}")).Should().Be(expected);

        var posted = await _client.PostAsync($"/api/data/{table}/query", Json($$"""{"filter":{"$type":"condition","column":"{{column}}","operator":"eq","value":{{json}} } }"""), ct);
        posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await posted.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("data").GetArrayLength().Should().Be(expected);

        var aggregated = await _client.PostAsync($"/api/data/{table}/aggregate",
            Json($$"""{"aggregations":[{"function":"count","alias":"n"}],"filter":[{"column":"{{column}}","operator":"eq","value":{{json}} }]}"""), ct);
        aggregated.StatusCode.Should().Be(HttpStatusCode.OK, await aggregated.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await aggregated.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("data")[0].GetProperty("n").GetInt64().Should().Be(expected);

        var literal = int.TryParse(value, out _) ? value : $"'{value}'";
        var odata = await _client.GetAsync($"/odata/{EntitySet(table)}?$filter={column} eq {literal}", ct);
        odata.StatusCode.Should().Be(HttpStatusCode.OK, await odata.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await odata.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("value").GetArrayLength().Should().Be(expected);
    }

    [Fact]
    public async Task A_derived_column_orders_a_list_and_an_odata_read()
    {
        var shop = await SetupAsync();

        (await ListValuesAsync(shop.Orders, "doubled", "doubled:desc")).Should().Equal("14", "10", "2");
        (await ListValuesAsync(shop.Orders, "customer_grade", "customer_grade:desc")).Should().Equal("B", "A", "A");
        (await ListValuesAsync(shop.Customers, "order_total", "order_total:asc")).Should().Equal("1", "12");

        var odata = await _client.GetAsync($"/odata/{EntitySet(shop.Orders)}?$orderby=doubled desc", TestContext.Current.CancellationToken);
        odata.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = JsonDocument.Parse(await odata.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement.GetProperty("value");
        rows.EnumerateArray().Select(r => r.GetProperty("doubled").GetInt32()).Should().Equal(14, 10, 2);
    }

    [Fact]
    public async Task A_derived_column_groups_and_is_aggregated()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SetupAsync();

        var grouped = await _client.PostAsync($"/api/data/{shop.Orders}/aggregate",
            Json("""{"aggregations":[{"function":"count","alias":"n"},{"function":"sum","column":"doubled","alias":"total"}],"groupBy":["customer_grade"],"orderBy":[{"column":"customer_grade","direction":"asc"}]}"""), ct);
        grouped.StatusCode.Should().Be(HttpStatusCode.OK, await grouped.Content.ReadAsStringAsync(ct));
        var groups = JsonDocument.Parse(await grouped.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("data");
        groups.EnumerateArray().Select(g => (g.GetProperty("customer_grade").GetString(), g.GetProperty("n").GetInt64(), g.GetProperty("total").GetInt64()))
            .Should().Equal(("A", 2L, 24L), ("B", 1L, 2L));

        var max = await _client.PostAsync($"/api/data/{shop.Customers}/aggregate",
            Json("""{"aggregations":[{"function":"max","column":"order_total","alias":"m"}]}"""), ct);
        max.StatusCode.Should().Be(HttpStatusCode.OK, await max.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await max.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("data")[0].GetProperty("m").GetInt64().Should().Be(12);
    }

    [Fact]
    public async Task Graphql_records_carries_filters_and_orders_by_each_kind_of_derived_column()
    {
        var shop = await SetupAsync();
        const string query =
            """
            query($table: String!, $filter: [String!], $orderBy: String) {
              records(table: $table, filter: $filter, orderBy: $orderBy) { totalCount edges { node { data } } }
            }
            """;

        var orders = (await PostGraphQlAsync(query, new { table = shop.Orders, filter = Conditions("customer_grade:eq:A"), orderBy = "doubled:desc" }))
            .GetProperty("records");
        orders.GetProperty("totalCount").GetInt32().Should().Be(2);
        Nodes(orders).Select(n => (n.GetProperty("customer_grade").GetString(), n.GetProperty("doubled").GetInt32()))
            .Should().Equal(("A", 14), ("A", 10));

        var customers = (await PostGraphQlAsync(query, new { table = shop.Customers, filter = Conditions("order_total:gt:5"), orderBy = "code" }))
            .GetProperty("records");
        Nodes(customers).Select(n => n.GetProperty("order_total").GetInt64()).Should().Equal(12L);

        var products = (await PostGraphQlAsync(query, new { table = shop.Products, filter = (string[]?)null, orderBy = "doubled:desc" }))
            .GetProperty("records");
        Nodes(products).Select(n => n.GetProperty("doubled").GetInt32()).Should().Equal(
            [18, 10], "a table whose only derived column is a formula is read like any other");
    }

    [Fact]
    public async Task A_single_record_read_by_graphql_or_odata_carries_each_kind_of_derived_column()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SetupAsync();
        const string query =
            """
            query($table: String!, $id: UUID!) { record(table: $table, id: $id) { data } }
            """;

        var order = (await PostGraphQlAsync(query, new { table = shop.Orders, id = shop.O1 })).GetProperty("record").GetProperty("data");
        order.GetProperty("customer_grade").GetString().Should().Be("A");
        order.GetProperty("doubled").GetInt32().Should().Be(10);
        (await PostGraphQlAsync(query, new { table = shop.Customers, id = shop.C1 })).GetProperty("record").GetProperty("data")
            .GetProperty("order_total").GetInt64().Should().Be(12);
        (await PostGraphQlAsync(query, new { table = shop.Products, id = shop.P1 })).GetProperty("record").GetProperty("data")
            .GetProperty("doubled").GetInt32().Should().Be(10);

        var byKey = await _client.GetAsync($"/odata/{EntitySet(shop.Orders)}({shop.O1})", ct);
        byKey.StatusCode.Should().Be(HttpStatusCode.OK, await byKey.Content.ReadAsStringAsync(ct));
        var row = JsonDocument.Parse(await byKey.Content.ReadAsStringAsync(ct)).RootElement;
        row.GetProperty("customer_grade").GetString().Should().Be("A");
        row.GetProperty("doubled").GetInt32().Should().Be(10);

        var customer = await _client.GetAsync($"/odata/{EntitySet(shop.Customers)}({shop.C1})", ct);
        customer.StatusCode.Should().Be(HttpStatusCode.OK, await customer.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await customer.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("order_total").GetInt64().Should().Be(12);
    }

    [Fact]
    public async Task A_posted_query_selects_and_orders_by_a_derived_column()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SetupAsync();

        var posted = await _client.PostAsync($"/api/data/{shop.Orders}/query",
            Json("""{"select":["doubled","customer_grade"],"orderBy":["customer_grade:desc","doubled:asc"]}"""), ct);
        posted.StatusCode.Should().Be(HttpStatusCode.OK, await posted.Content.ReadAsStringAsync(ct));
        JsonDocument.Parse(await posted.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("data"))
            .Select(d => (d.GetProperty("customer_grade").GetString(), d.GetProperty("doubled").GetInt32()))
            .Should().Equal(("B", 2), ("A", 10), ("A", 14));
    }

    [Fact]
    public async Task Graphql_aggregate_groups_by_and_sums_a_derived_column()
    {
        var shop = await SetupAsync();

        var data = (await PostGraphQlAsync(
            """
            query($table: String!) {
              aggregate(table: $table,
                        aggregations: [{ function: COUNT, alias: "n" }, { function: SUM, column: "doubled", alias: "total" }],
                        groupBy: ["customer_grade"],
                        filter: [{ column: "doubled", operator: "gt", value: 1 }],
                        orderBy: [{ column: "customer_grade", direction: "desc" }]) { data }
            }
            """,
            new { table = shop.Orders })).GetProperty("aggregate").GetProperty("data");

        data.EnumerateArray().Select(g => (g.GetProperty("customer_grade").GetString(), g.GetProperty("n").GetInt64(), g.GetProperty("total").GetInt64()))
            .Should().Equal(("B", 1L, 2L), ("A", 2L, 24L));
    }

    [Theory]
    [InlineData("{no_such} * 2", "a column the table does not have")]
    [InlineData("{amount} * ", "a formula that does not parse")]
    [InlineData("{label} - 1", "arithmetic PostgreSQL cannot type (text minus integer)")]
    [InlineData("{label_grade} || 'x'", "a derived column — a formula computes over stored columns")]
    public async Task A_formula_that_cannot_be_computed_is_refused_when_declared(string formula, string why)
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var target = $"vc_tgt_{s}";
        var table = $"vc_bad_{s}";
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = target,
            Columns = [new CreateColumnApiRequest { Name = "grade", Type = "text" }],
        }, ct));
        await CreatedAsync(_client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "ref_id", Type = "uuid" },
                new CreateColumnApiRequest { Name = "amount", Type = "integer" },
                new CreateColumnApiRequest { Name = "label", Type = "text" },
                new CreateColumnApiRequest
                {
                    Name = "label_grade", Type = "text",
                    Lookup = new LookupConfigApiRequest { RelationColumn = "ref_id", TargetTable = target, TargetColumn = "grade" },
                },
            ],
        }, ct));

        var added = await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns", new AddColumnApiRequest
        {
            Name = "computed",
            Type = "integer",
            Formula = new FormulaConfigApiRequest { Formula = formula, ReturnType = "integer" },
        }, ct);

        added.StatusCode.Should().Be(HttpStatusCode.BadRequest, why);
        (await added.Content.ReadAsStringAsync(ct)).Should().Contain("INVALID_EXPRESSION");
        (await _client.GetAsync($"/api/data/{table}", ct)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a refused formula was never declared, so the table still reads");

        // The same formula declared with a new table (which has no lookup to name, so that case
        // names a missing column instead).
        var declared = formula.Contains("{label_grade}", StringComparison.Ordinal) ? "{no_such} * 2" : formula;
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = $"vc_new_{s}",
            Columns =
            [
                new CreateColumnApiRequest { Name = "amount", Type = "integer" },
                new CreateColumnApiRequest { Name = "label", Type = "text" },
                new CreateColumnApiRequest { Name = "computed", Type = "integer", Formula = new FormulaConfigApiRequest { Formula = declared, ReturnType = "integer" } },
            ],
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a table is refused whole when one of its formulas cannot run");
        (await _client.GetAsync($"/api/schema/tables/vc_new_{s}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "nothing of the refused table was kept");
    }

    /// <summary>
    /// A derived column is computed by reads; an UPDATE or DELETE addresses the stored table, where it
    /// does not exist. Narrowing a bulk write by one is the caller's mistake, said as such — not a
    /// database error surfaced as a 500.
    /// </summary>
    [Fact]
    public async Task A_derived_column_cannot_narrow_a_bulk_delete()
    {
        var ct = TestContext.Current.CancellationToken;
        var shop = await SetupAsync();

        var response = await _client.DeleteAsync($"/api/batch/data/{shop.Orders}?filter=doubled:gt:5", ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(ct));
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("VALIDATION_ERROR");
        (await CountRowsAsync($"/api/data/{shop.Orders}")).Should().Be(3, "nothing was deleted");
    }

    private static string Table(Shop shop, string which) => which switch
    {
        "orders" => shop.Orders,
        "customers" => shop.Customers,
        _ => shop.Products,
    };

    private static string EntitySet(string table) =>
        string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static IEnumerable<JsonElement> Nodes(JsonElement connection) =>
        connection.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("node").GetProperty("data"));

    private static string[] Conditions(params string[] conditions) => conditions;

    private async Task<JsonElement> PostGraphQlAsync(string query, object variables)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables }, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, payload);
        var root = JsonDocument.Parse(payload).RootElement.Clone();
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"the read must resolve: {errors}");
        return root.GetProperty("data");
    }

    private static async Task CreatedAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<string> InsertAsync(string table, object row)
    {
        var response = await _client.PostAsJsonAsync($"/api/data/{table}", row, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> GetDataAsync(string url)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();
    }

    private async Task<int> CountRowsAsync(string url)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetArrayLength();
    }

    /// <summary>The values of <paramref name="column"/> in list order, ordered by <paramref name="orderBy"/>.</summary>
    private async Task<IReadOnlyList<string>> ListValuesAsync(string table, string column, string orderBy)
    {
        var order = orderBy.Contains(':', StringComparison.Ordinal) ? orderBy : $"{orderBy}:asc";
        var response = await _client.GetAsync($"/api/data/{table}?orderBy={order}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(r => r.GetProperty("data").GetProperty(column))
            .Select(v => v.ValueKind == JsonValueKind.Null ? "null" : v.ToString())
            .ToList();
    }
}
