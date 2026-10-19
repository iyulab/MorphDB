using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// What a rollup reads with nothing to summarise, and while its target cannot answer.
/// <para>
/// Over no rows a count is 0 and every other aggregation reads null — the sum included, as SQL's
/// aggregates read over an empty set: a total of nothing is not a total of zero. A rollup declared
/// with <c>whenTargetMissing: "empty"</c> is accepted while its target does not exist, or lacks a
/// column the rollup names, and reads what a row no target row points at reads until the target
/// answers — the counterpart of a lookup's <c>"null"</c>. Without it, the rollup is refused when
/// declared and fails reads, naming the target. A source column of a type the aggregation does not
/// take is a wrong declaration, not a missing target, and is refused either way.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class RollupTargetMissingTests
{
    private readonly HttpClient _client;

    public RollupTargetMissingTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static CreateColumnApiRequest Rollup(string name, string type, string orders, string aggregation, string source, string? whenTargetMissing) => new()
    {
        Name = name,
        Type = type,
        Rollup = new RollupConfigApiRequest
        {
            Relation = "customer_orders",
            TargetTable = orders,
            ForeignKeyColumn = "customer_id",
            SourceColumn = source,
            Aggregation = aggregation,
            WhenTargetMissing = whenTargetMissing,
        },
    };

    private async Task<string> CreateCustomersAsync(string orders, string? whenTargetMissing, HttpStatusCode expected, string totalSource = "amount")
    {
        var ct = TestContext.Current.CancellationToken;
        var customers = $"rtm_cust_{Suffix()}";
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customers,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text" },
                Rollup("order_count", "integer", orders, "count", "*", whenTargetMissing),
                Rollup("order_total", "decimal", orders, "sum", totalSource, whenTargetMissing),
                Rollup("order_average", "decimal", orders, "average", totalSource, whenTargetMissing),
            ],
        }, ct);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(ct));
        return customers;
    }

    private async Task CreateOrdersAsync(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_id", Type = "uuid" },
                new CreateColumnApiRequest { Name = "amount", Type = "decimal" },
                new CreateColumnApiRequest { Name = "label", Type = "text" },
            ],
        }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task A_row_nothing_points_at_counts_zero_and_sums_nothing()
    {
        var orders = $"rtm_ord_{Suffix()}";
        await CreateOrdersAsync(orders);
        var customers = await CreateCustomersAsync(orders, whenTargetMissing: null, HttpStatusCode.Created);
        var lonely = Guid.NewGuid();
        var busy = Guid.NewGuid();
        await InsertAsync(customers, new { _id = lonely, name = "a-lonely" });
        await InsertAsync(customers, new { _id = busy, name = "b-busy" });
        await InsertAsync(orders, new { customer_id = busy, amount = 10m });
        await InsertAsync(orders, new { customer_id = busy, amount = 30m });

        var rows = await ListAsync(customers);

        rows[0].GetProperty("order_count").GetInt64().Should().Be(0);
        rows[0].GetProperty("order_total").ValueKind.Should().Be(JsonValueKind.Null, "a total of no rows is not a total of zero");
        rows[0].GetProperty("order_average").ValueKind.Should().Be(JsonValueKind.Null);
        rows[1].GetProperty("order_count").GetInt64().Should().Be(2);
        rows[1].GetProperty("order_total").GetDecimal().Should().Be(40m);
        rows[1].GetProperty("order_average").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task By_default_a_rollup_over_a_table_that_does_not_exist_is_refused()
    {
        await CreateCustomersAsync($"rtm_none_{Suffix()}", whenTargetMissing: null, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_rollup_declared_before_its_target_summarises_nothing_then_the_target_once_it_exists()
    {
        var orders = $"rtm_ord_{Suffix()}";
        var customers = await CreateCustomersAsync(orders, "empty", HttpStatusCode.Created);
        var id = Guid.NewGuid();
        await InsertAsync(customers, new { _id = id, name = "c" });

        var before = (await ListAsync(customers)).Single();
        before.GetProperty("order_count").GetInt64().Should().Be(0);
        before.GetProperty("order_total").ValueKind.Should().Be(JsonValueKind.Null);
        (await ListAsync($"{customers}?filter=order_count:eq:0")).Should().ContainSingle("a filter over the column plans while the target is absent");

        await CreateOrdersAsync(orders);
        await InsertAsync(orders, new { customer_id = id, amount = 5m });

        var after = (await ListAsync(customers)).Single();
        after.GetProperty("order_count").GetInt64().Should().Be(1);
        after.GetProperty("order_total").GetDecimal().Should().Be(5m);
    }

    [Fact]
    public async Task A_target_dropped_and_rebuilt_summarises_nothing_in_between()
    {
        var ct = TestContext.Current.CancellationToken;
        var orders = $"rtm_ord_{Suffix()}";
        await CreateOrdersAsync(orders);
        var customers = await CreateCustomersAsync(orders, "empty", HttpStatusCode.Created);
        var id = Guid.NewGuid();
        await InsertAsync(customers, new { _id = id, name = "c" });
        await InsertAsync(orders, new { customer_id = id, amount = 5m });

        (await _client.DeleteAsync($"/api/schema/tables/{orders}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var between = (await ListAsync(customers)).Single();
        between.GetProperty("order_count").GetInt64().Should().Be(0, "the read does not fail while the target is being rebuilt");
        between.GetProperty("order_total").ValueKind.Should().Be(JsonValueKind.Null);

        await CreateOrdersAsync(orders);
        await InsertAsync(orders, new { customer_id = id, amount = 7m });
        (await ListAsync(customers)).Single().GetProperty("order_total").GetDecimal().Should().Be(7m);
    }

    [Fact]
    public async Task Without_the_option_a_dropped_target_fails_the_read_naming_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var orders = $"rtm_ord_{Suffix()}";
        await CreateOrdersAsync(orders);
        var customers = await CreateCustomersAsync(orders, whenTargetMissing: null, HttpStatusCode.Created);

        (await _client.DeleteAsync($"/api/schema/tables/{orders}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync($"/api/data/{customers}", ct);
        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(orders);
    }

    [Fact]
    public async Task A_source_column_the_aggregation_cannot_take_is_refused_even_with_the_option()
    {
        var orders = $"rtm_ord_{Suffix()}";
        await CreateOrdersAsync(orders);

        // A sum over a text column: the target answers, and the declaration is wrong.
        await CreateCustomersAsync(orders, "empty", HttpStatusCode.BadRequest, totalSource: "label");
    }

    [Fact]
    public async Task The_option_is_read_back_and_the_default_is_named()
    {
        var ct = TestContext.Current.CancellationToken;
        var orders = $"rtm_ord_{Suffix()}";
        await CreateOrdersAsync(orders);
        var lenient = await CreateCustomersAsync(orders, "empty", HttpStatusCode.Created);
        var strict = await CreateCustomersAsync(orders, whenTargetMissing: null, HttpStatusCode.Created);

        foreach (var (table, expected) in new[] { (lenient, "empty"), (strict, "fail") })
        {
            var response = await _client.GetFromJsonAsync<JsonElement>($"/api/schema/tables/{table}", ct);
            var column = response.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "order_count");
            column.GetProperty("rollup").GetProperty("whenTargetMissing").GetString().Should().Be(expected);
        }
    }

    private async Task InsertAsync(string table, object row) =>
        (await _client.PostAsJsonAsync($"/api/data/{table}", row, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

    private async Task<IReadOnlyList<JsonElement>> ListAsync(string tableAndQuery)
    {
        var separator = tableAndQuery.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var response = await _client.GetAsync($"/api/data/{tableAndQuery}{separator}orderBy=name:asc", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("data").Clone()).ToList();
    }
}
