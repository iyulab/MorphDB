using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using ClientModels = MorphDB.Client.Models;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The <c>exists</c> condition: a row is kept when a row of another table holds its value in a column and
/// meets the related conditions — the orders with a line over four, as OData's <c>any()</c> asks. Every
/// read that takes a filter answers it alike (a row list, a query body, an aggregate, the .NET client); a
/// write's filter refuses it, and the related table is read as any read of it is.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ExistsFilterContractTests(ApiIntegrationFixture fixture)
{
    private readonly HttpClient _client = fixture.Api.Client;

    private async Task<(string Orders, string Lines)> OrdersAndLinesAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var orders = $"ex_orders_{suffix}";
        var lines = $"ex_lines_{suffix}";
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns = [new CreateColumnApiRequest { Name = "order_no", Type = "text", Nullable = false }],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = lines,
            Columns =
            [
                new CreateColumnApiRequest { Name = "order_no", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "product", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = false },
            ],
            SystemColumns = new SystemColumnOptionsApiRequest { SoftDelete = true },
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var order in new[] { "A-1", "A-2", "A-3" })
        {
            (await _client.PostAsJsonAsync($"/api/data/{orders}", new Dictionary<string, object?> { ["order_no"] = order }, ct)).EnsureSuccessStatusCode();
        }

        foreach (var (order, product, qty) in new[] { ("A-1", "nut", 5), ("A-1", "bolt", 1), ("A-2", "bolt", 2), ("A-2", "nut", 9) })
        {
            (await _client.PostAsJsonAsync($"/api/data/{lines}", new Dictionary<string, object?> { ["order_no"] = order, ["product"] = product, ["qty"] = qty }, ct)).EnsureSuccessStatusCode();
        }

        return (orders, lines);
    }

    private static string Exists(string lines, params string[] filter) =>
        "order_no:exists:" + JsonSerializer.Serialize(new { table = lines, column = "order_no", filter });

    private async Task<string[]> OrdersAsync(string orders, CancellationToken ct, params string[] conditions)
    {
        var query = string.Join("&", conditions.Select(c => "filter=" + Uri.EscapeDataString(c)));
        var response = await _client.GetAsync($"/api/data/{orders}?{query}&orderBy=order_no:asc", ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return [.. body.RootElement.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("data").GetProperty("order_no").GetString()!)];
    }

    [Fact]
    public async Task A_row_list_keeps_the_rows_with_a_related_row_the_conditions_keep()
    {
        var ct = TestContext.Current.CancellationToken;
        var (orders, lines) = await OrdersAndLinesAsync(ct);

        (await OrdersAsync(orders, ct, Exists(lines, "qty:gt:4"))).Should().Equal("A-1", "A-2");
        (await OrdersAsync(orders, ct, Exists(lines, "qty:gt:4", "product:eq:nut"), "order_no:neq:A-2")).Should().Equal("A-1");
        (await OrdersAsync(orders, ct, Exists(lines))).Should().Equal(["A-1", "A-2"], "with no conditions, any related row");
        (await OrdersAsync(orders, ct, Exists(lines, "product:in:[\"washer\"]"))).Should().BeEmpty();
    }

    [Fact]
    public async Task A_soft_deleted_related_row_does_not_count()
    {
        var ct = TestContext.Current.CancellationToken;
        var (orders, lines) = await OrdersAndLinesAsync(ct);
        var nine = await _client.GetAsync($"/api/data/{lines}?filter={Uri.EscapeDataString("qty:eq:9")}", ct);
        using var body = JsonDocument.Parse(await nine.Content.ReadAsStringAsync(ct));
        var id = body.RootElement.GetProperty("data")[0].GetProperty("id").GetString();
        (await _client.DeleteAsync($"/api/data/{lines}/{id}", ct)).IsSuccessStatusCode.Should().BeTrue();

        (await OrdersAsync(orders, ct, Exists(lines, "qty:gt:4"))).Should().Equal("A-1");
    }

    [Fact]
    public async Task A_query_body_and_an_aggregate_take_it_as_an_object()
    {
        var ct = TestContext.Current.CancellationToken;
        var (orders, lines) = await OrdersAndLinesAsync(ct);
        var related = new { table = lines, column = "order_no", filter = new object[] { "qty:gt:4", new { column = "product", @operator = "eq", value = "nut" } } };

        var queried = await _client.PostAsJsonAsync($"/api/data/{orders}/query",
            new { filter = new Dictionary<string, object?> { ["$type"] = "condition", ["column"] = "order_no", ["operator"] = "exists", ["value"] = related } }, ct);
        queried.StatusCode.Should().Be(HttpStatusCode.OK, await queried.Content.ReadAsStringAsync(ct));
        using (var body = JsonDocument.Parse(await queried.Content.ReadAsStringAsync(ct)))
        {
            body.RootElement.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("data").GetProperty("order_no").GetString())
                .Should().BeEquivalentTo("A-1", "A-2");
        }

        var counted = await _client.PostAsJsonAsync($"/api/data/{orders}/aggregate", new
        {
            aggregations = new[] { new { function = "count", alias = "n" } },
            filter = new[] { new { column = "order_no", @operator = "exists", value = (object)related } },
        }, ct);
        counted.StatusCode.Should().Be(HttpStatusCode.OK, await counted.Content.ReadAsStringAsync(ct));
        using var aggregate = JsonDocument.Parse(await counted.Content.ReadAsStringAsync(ct));
        aggregate.RootElement.GetProperty("data")[0].GetProperty("n").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task The_client_sends_related_rows_as_the_server_reads_them()
    {
        var ct = TestContext.Current.CancellationToken;
        var (orders, lines) = await OrdersAndLinesAsync(ct);
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        var exists = new ClientModels.Filter("order_no", ClientModels.FilterOperator.Exists,
            new ClientModels.RelatedRows(lines, "order_no", [new ClientModels.Filter("qty", ClientModels.FilterOperator.GreaterThan, 4)]));

        var page = await client.Data.QueryAsync(orders, new ClientModels.QueryRequest { Filters = [exists], OrderBy = [new ClientModels.OrderBy("order_no")] }, ct);
        page.Data.Select(r => r.Data["order_no"]?.ToString()).Should().Equal("A-1", "A-2");

        var counted = await client.Data.AggregateAsync(orders, new ClientModels.AggregationRequest
        {
            Aggregations = [ClientModels.AggregationColumn.Count("n")],
            Filter = [new ClientModels.AggregationFilter("order_no", ClientModels.FilterOperator.Exists, exists.Value)],
        }, ct);
        Convert.ToInt64(counted.Data.Single()["n"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(2);
    }

    [Fact]
    public async Task What_exists_cannot_answer_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (orders, lines) = await OrdersAndLinesAsync(ct);

        async Task<HttpStatusCode> Status(string condition) =>
            (await _client.GetAsync($"/api/data/{orders}?filter={Uri.EscapeDataString(condition)}", ct)).StatusCode;

        (await Status("order_no:exists:not-json")).Should().Be(HttpStatusCode.BadRequest);
        (await Status("order_no:exists:{\"table\":\"" + lines + "\"}")).Should().Be(HttpStatusCode.BadRequest, "the related column is required");
        (await Status(Exists(lines, "nope:eq:1"))).Should().NotBe(HttpStatusCode.OK, "a related column the table does not have");
        (await Status(Exists(lines, Exists(lines)))).Should().Be(HttpStatusCode.BadRequest, "an exists inside an exists");

        var write = await _client.DeleteAsync($"/api/batch/data/{orders}?filter={Uri.EscapeDataString(Exists(lines, "qty:gt:4"))}", ct);
        write.StatusCode.Should().Be(HttpStatusCode.BadRequest, "exists narrows reads, not writes");
        (await OrdersAsync(orders, ct)).Should().HaveCount(3, "the refused delete wrote nothing");
    }
}
