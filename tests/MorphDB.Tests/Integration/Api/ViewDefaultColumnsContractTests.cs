using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A view that names no columns. It is a legitimate request — the body of a plain view over a table needs nothing
/// else — and it has to mean something a caller can use: every column of the base table, by the names the table's
/// own reads use. It used to be created (<c>201</c>) as <c>SELECT *</c>, whose rows carry the table's physical
/// names (<c>col_e9af1e674b83</c>), so the view could not be read by the names it was built from: a filter on
/// <c>code</c> was a <c>500</c> from the database.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ViewDefaultColumnsContractTests
{
    private readonly HttpClient _client;

    public ViewDefaultColumnsContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_view_that_names_no_columns_reads_the_base_table_by_its_own_names()
    {
        var (table, view) = await SeedViewAsync(withColumns: false);

        var response = await _client.GetAsync($"/api/views/{view}/data?take=100&filter={Uri.EscapeDataString("code:eq:12345")}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var rows = JsonDocument.Parse(text).RootElement.GetProperty("data");
        rows.GetArrayLength().Should().Be(1);
        var names = rows[0].EnumerateObject().Select(p => p.Name).ToArray();
        names.Should().Contain(["_id", "code", "qty"]).And.NotContain(n => n.StartsWith("col_", StringComparison.Ordinal),
            $"the rows of a view over {table} carry the names the table's reads do, not its physical ones");
        rows[0].GetProperty("qty").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task The_view_reports_the_columns_it_was_given_with_their_types()
    {
        var (_, view) = await SeedViewAsync(withColumns: false);

        var response = await _client.GetAsync($"/api/views/{view}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var columns = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("columns")
            .EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("dataType").GetString()!);
        columns.Should().ContainKey("code").WhoseValue.Should().Be("Text");
        columns.Should().ContainKey("qty").WhoseValue.Should().Be("Integer");
        columns.Keys.Should().Contain("_id");
    }

    [Fact]
    public async Task A_column_taken_from_the_table_reports_the_type_of_its_source()
    {
        var (table, _) = await SeedViewAsync(withColumns: false);
        var view = $"vt_{table}"[..30];
        (await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = view,
            BaseTable = table,
            Columns = [new ViewColumnApiSpec { Source = "qty", Alias = "quantity" }, new ViewColumnApiSpec { Source = "code", Alias = "label", DataType = "longtext" }],
        }, Ct)).EnsureSuccessStatusCode();

        var columns = JsonDocument.Parse(await (await _client.GetAsync($"/api/views/{view}", Ct)).Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("columns")
            .EnumerateArray().ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("dataType").GetString()!);

        columns["quantity"].Should().Be("Integer", "an integer column read through a view is still an integer");
        columns["label"].Should().Be("LongText", "a type the request states is the type it gets");
    }

    [Fact]
    public async Task A_filter_on_a_name_the_view_does_not_have_is_refused()
    {
        var (_, view) = await SeedViewAsync(withColumns: false);

        var response = await _client.GetAsync($"/api/views/{view}/data?filter={Uri.EscapeDataString("nope:eq:1")}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_view_that_names_columns_keeps_to_them()
    {
        var (_, view) = await SeedViewAsync(withColumns: true);

        var rows = JsonDocument.Parse(await (await _client.GetAsync($"/api/views/{view}/data?take=1", Ct)).Content.ReadAsStringAsync(Ct))
            .RootElement.GetProperty("data");

        rows[0].EnumerateObject().Select(p => p.Name).Should().Equal("code");
    }

    /// <summary>The view API.md shows — its shape, over tables of its own — is created and read.</summary>
    [Fact]
    public async Task The_view_the_docs_show_can_be_created_and_read()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orders, customer, view) = ($"orders_{suffix}", $"customer_{suffix}", $"open_orders_{suffix}");
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customer,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = true }],
        }, Ct)).EnsureSuccessStatusCode();
        var customerRow = (await _client.PostAsJsonAsync($"/api/data/{customer}", new Dictionary<string, object?> { ["name"] = "Acme" }, Ct)).EnsureSuccessStatusCode();
        var customerId = (await customerRow.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns =
            [
                new CreateColumnApiRequest { Name = "order_no", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "status", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "price", Type = "decimal", Nullable = true },
                new CreateColumnApiRequest { Name = "customer_id", Type = "uuid", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{orders}", new Dictionary<string, object?>
        {
            ["order_no"] = "A-1", ["status"] = "open", ["qty"] = 3, ["price"] = 2.5m, ["customer_id"] = customerId,
        }, Ct)).EnsureSuccessStatusCode();

        // The request body of API.md's example, with the tables of this test.
        var created = await _client.PostAsync("/api/views", new StringContent($$"""
            {
              "name": "{{view}}",
              "baseTable": "{{orders}}",
              "columns": [
                { "source": "order_no", "alias": "order_no" },
                { "source": "{{customer}}.name", "alias": "customer" },
                { "expression": "qty * price", "alias": "total", "dataType": "decimal" }
              ],
              "joins": [ { "table": "{{customer}}", "condition": "{{orders}}.customer_id = {{customer}}._id" } ],
              "filters": [ { "field": "status", "operator": "eq", "value": "open" } ],
              "orderBy": [ { "column": "order_no" } ]
            }
            """, System.Text.Encoding.UTF8, "application/json"), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));

        var rows = JsonDocument.Parse(await (await _client.GetAsync($"/api/views/{view}/data", Ct)).Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("data");
        rows.GetArrayLength().Should().Be(1);
        rows[0].GetProperty("customer").GetString().Should().Be("Acme");
        rows[0].GetProperty("total").GetDecimal().Should().Be(7.5m);
    }

    private async Task<(string Table, string View)> SeedViewAsync(bool withColumns)
    {
        var table = $"vcols_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert",
            new[] { new Dictionary<string, object?> { ["code"] = "12345", ["qty"] = 1 }, new Dictionary<string, object?> { ["code"] = "007", ["qty"] = 2 } }, Ct)).EnsureSuccessStatusCode();

        var view = $"v_{table}"[..30];
        (await _client.PostAsJsonAsync("/api/views", new CreateViewApiRequest
        {
            Name = view,
            BaseTable = table,
            Columns = withColumns ? [new ViewColumnApiSpec { Source = "code", Alias = "code" }] : [],
        }, Ct)).EnsureSuccessStatusCode();
        return (table, view);
    }
}
