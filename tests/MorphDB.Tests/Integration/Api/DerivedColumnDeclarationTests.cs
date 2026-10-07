using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// What a rollup or lookup declares reaches SQL only in a form the server built: column names
/// resolved to the target table's columns, values rendered as literals, an order parsed — and a
/// declaration the read could not compute is refused when it is made.
/// <para>
/// Before, a rollup's order was pasted into the statement as the caller wrote it, a filter value
/// went in as raw text (a JSON string arrived unquoted), and the filter's column was emitted in its
/// logical name — so a filtered rollup never ran, while the text around it was the caller's to
/// write. A lookup or rollup whose target did not resolve was skipped on every read, so the column
/// silently vanished from the rows.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class DerivedColumnDeclarationTests
{
    private readonly HttpClient _client;

    public DerivedColumnDeclarationTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private async Task<(string Customers, string Orders, string C1)> SetupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = Guid.NewGuid().ToString("N")[..8];
        var customers = $"dc_cust_{s}";
        var orders = $"dc_ord_{s}";
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customers,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text" }],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_id", Type = "uuid" },
                new CreateColumnApiRequest { Name = "amount", Type = "integer" },
                new CreateColumnApiRequest { Name = "note", Type = "text" },
            ],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        var c1 = await InsertAsync(customers, new { code = "C-1" });
        await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c1, ["amount"] = 5, ["note"] = "plain" });
        await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c1, ["amount"] = 7, ["note"] = "it's quoted" });
        await InsertAsync(orders, new Dictionary<string, object?> { ["customer_id"] = c1, ["amount"] = 1, ["note"] = "plain" });
        return (customers, orders, c1);
    }

    [Fact]
    public async Task A_filtered_ordered_rollup_computes_from_the_columns_it_names()
    {
        var (customers, orders, c1) = await SetupAsync();

        await AddRollupAsync(customers, "big_total", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "sum",
            Filter = new RollupFilterApiRequest { Field = "amount", Operator = "gt", Value = 4 },
        }, HttpStatusCode.Created);
        await AddRollupAsync(customers, "quoted_count", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "countValues",
            Filter = new RollupFilterApiRequest { Field = "note", Operator = "eq", Value = "it's quoted" },
        }, HttpStatusCode.Created);
        await AddRollupAsync(customers, "amounts_desc", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "note",
            Aggregation = "stringConcat",
            Delimiter = "|",
            OrderBy = "amount desc",
        }, HttpStatusCode.Created);

        var row = await GetDataAsync($"/api/data/{customers}/{c1}");
        row.GetProperty("big_total").GetInt64().Should().Be(12, "the filter keeps the orders above 4");
        row.GetProperty("quoted_count").GetInt64().Should().Be(1, "a quote inside the value is part of the value");
        row.GetProperty("amounts_desc").GetString().Should().Be("it's quoted|plain|plain", "collected in the declared order (amount 7, 5, 1)");
    }

    [Theory]
    [InlineData("amount); DROP TABLE dc_never", "an order is parsed, never pasted")]
    [InlineData("amount sideways", "an order term is a column and a direction")]
    [InlineData("no_such desc", "an order names a column of the target")]
    public async Task A_rollup_order_that_is_not_a_column_list_is_refused(string orderBy, string why)
    {
        var (customers, orders, _) = await SetupAsync();

        var body = await AddRollupAsync(customers, "ordered", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "arrayValues",
            OrderBy = orderBy,
        }, HttpStatusCode.BadRequest);

        body.Should().Contain("INVALID_EXPRESSION", why);
        (await _client.GetAsync($"/api/data/{customers}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rollup_filter_value_is_a_literal_or_refused()
    {
        var (customers, orders, c1) = await SetupAsync();

        // A string that would close the predicate if it were pasted is just a value that matches nothing.
        await AddRollupAsync(customers, "hostile_count", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "countValues",
            Filter = new RollupFilterApiRequest { Field = "note", Operator = "eq", Value = "x' OR '1'='1" },
        }, HttpStatusCode.Created);
        (await GetDataAsync($"/api/data/{customers}/{c1}")).GetProperty("hostile_count").GetInt64().Should().Be(0);

        // A value with no literal form is refused.
        var body = await AddRollupAsync(customers, "object_value", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "sum",
            Filter = new RollupFilterApiRequest { Field = "amount", Operator = "eq", Value = new { nested = 1 } },
        }, HttpStatusCode.BadRequest);
        body.Should().Contain("INVALID_EXPRESSION");

        var unknownField = await AddRollupAsync(customers, "unknown_field", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "sum",
            Filter = new RollupFilterApiRequest { Field = "no_such", Operator = "eq", Value = 1 },
        }, HttpStatusCode.BadRequest);
        unknownField.Should().Contain("INVALID_EXPRESSION");
    }

    [Fact]
    public async Task A_rollup_aggregation_the_server_does_not_know_is_refused_instead_of_counting()
    {
        var (customers, orders, _) = await SetupAsync();

        var body = await AddRollupAsync(customers, "median_amount", new RollupConfigApiRequest
        {
            Relation = orders,
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = "amount",
            Aggregation = "median",
        }, HttpStatusCode.BadRequest);

        body.Should().Contain("median", "the refusal names what it did not know");
    }

    [Fact]
    public async Task A_lookup_whose_target_does_not_resolve_is_refused_instead_of_vanishing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, orders, _) = await SetupAsync();

        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{orders}/columns", new AddColumnApiRequest
        {
            Name = "ghost",
            Type = "text",
            Lookup = new LookupConfigApiRequest { RelationColumn = "customer_id", TargetTable = "no_such_table", TargetColumn = "code" },
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(ct));
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("INVALID_EXPRESSION");
    }

    [Fact]
    public async Task A_table_may_look_up_its_own_rows_when_it_is_declared()
    {
        var ct = TestContext.Current.CancellationToken;
        var people = $"dc_people_{Guid.NewGuid().ToString("N")[..8]}";

        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = people,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text" },
                new CreateColumnApiRequest { Name = "manager_id", Type = "uuid" },
                new CreateColumnApiRequest
                {
                    Name = "manager_name", Type = "text",
                    Lookup = new LookupConfigApiRequest { RelationColumn = "manager_id", TargetTable = people, TargetColumn = "name" },
                },
            ],
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        var boss = await InsertAsync(people, new { name = "Boss" });
        var worker = await InsertAsync(people, new Dictionary<string, object?> { ["name"] = "Worker", ["manager_id"] = boss });
        (await GetDataAsync($"/api/data/{people}/{worker}")).GetProperty("manager_name").GetString().Should().Be("Boss");
    }

    private async Task<string> AddRollupAsync(string table, string name, RollupConfigApiRequest rollup, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync($"/api/schema/tables/{table}/columns",
            new AddColumnApiRequest { Name = name, Type = "text", Rollup = rollup }, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, body);
        return body;
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
}
