using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Client;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using ClientModels = MorphDB.Client.Models;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A lookup declared with <c>whenTargetMissing: "null"</c> is accepted while its target table does
/// not exist and reads null until it does — from then on it reads the target as usual, and when the
/// target is dropped and created again it reads null in between. That is the life of a target a
/// writer builds in an order it does not control, or rebuilds by dropping and recreating it. Without
/// it, a lookup is refused when declared and fails reads, naming the target.
/// <para>
/// Only the table's absence is covered: a target that exists without the read column is a
/// declaration that no longer fits its target, and is refused either way.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class LookupTargetMissingTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public LookupTargetMissingTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static LookupConfigApiRequest GradeLookup(string target, string? whenTargetMissing) => new()
    {
        RelationColumn = "customer_code",
        TargetTable = target,
        TargetColumn = "grade",
        MatchColumn = "code",
        WhenTargetMissing = whenTargetMissing,
    };

    private async Task<string> CreateOrdersAsync(string target, string? whenTargetMissing, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var orders = $"ltm_ord_{Suffix()}";
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = orders,
            Columns =
            [
                new CreateColumnApiRequest { Name = "customer_code", Type = "text" },
                new CreateColumnApiRequest { Name = "customer_grade", Type = "text", Lookup = GradeLookup(target, whenTargetMissing) },
            ],
        }, ct);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(ct));
        return orders;
    }

    private async Task CreateCustomersAsync(string name, params (string Code, string Grade)[] rows)
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text" }, new CreateColumnApiRequest { Name = "grade", Type = "text" }],
        }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        foreach (var (code, grade) in rows)
        {
            await InsertAsync(name, new { code, grade });
        }
    }

    [Fact]
    public async Task By_default_a_lookup_to_a_table_that_does_not_exist_is_refused()
    {
        await CreateOrdersAsync($"ltm_none_{Suffix()}", whenTargetMissing: null, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_lookup_declared_before_its_target_reads_null_then_the_target_once_it_exists()
    {
        var customers = $"ltm_cust_{Suffix()}";
        var orders = await CreateOrdersAsync(customers, "null", HttpStatusCode.Created);
        await InsertAsync(orders, new { customer_code = "C-1" });
        await InsertAsync(orders, new { customer_code = "C-2" });

        var before = await ListAsync(orders);
        before.Should().HaveCount(2);
        before.Should().AllSatisfy(r => r.GetProperty("customer_grade").ValueKind.Should().Be(JsonValueKind.Null));
        (await ListAsync($"{orders}?filter=customer_grade:isnull")).Should().HaveCount(2, "a filter over the column plans while the target is absent");

        await CreateCustomersAsync(customers, ("C-1", "gold"));

        (await ListAsync(orders)).Select(r => r.GetProperty("customer_grade").ToString()).Should().Equal("gold", "");
        (await ListAsync($"{orders}?filter=customer_grade:eq:gold")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_target_dropped_and_rebuilt_reads_null_in_between_and_its_new_rows_after()
    {
        var ct = TestContext.Current.CancellationToken;
        var customers = $"ltm_cust_{Suffix()}";
        await CreateCustomersAsync(customers, ("C-1", "gold"));
        var orders = await CreateOrdersAsync(customers, "null", HttpStatusCode.Created);
        await InsertAsync(orders, new { customer_code = "C-1" });
        (await ListAsync(orders)).Single().GetProperty("customer_grade").GetString().Should().Be("gold");

        (await _client.DeleteAsync($"/api/schema/tables/{customers}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(orders)).Single().GetProperty("customer_grade").ValueKind.Should().Be(JsonValueKind.Null,
            "the read does not fail while the target is being rebuilt");

        await CreateCustomersAsync(customers, ("C-1", "silver"));
        (await ListAsync(orders)).Single().GetProperty("customer_grade").GetString().Should().Be("silver");
    }

    [Fact]
    public async Task Without_the_option_a_dropped_target_fails_the_read_naming_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var customers = $"ltm_cust_{Suffix()}";
        await CreateCustomersAsync(customers, ("C-1", "gold"));
        var orders = await CreateOrdersAsync(customers, whenTargetMissing: null, HttpStatusCode.Created);

        (await _client.DeleteAsync($"/api/schema/tables/{customers}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.GetAsync($"/api/data/{orders}", ct);
        response.IsSuccessStatusCode.Should().BeFalse();
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(customers);
    }

    [Fact]
    public async Task A_target_that_exists_without_the_read_column_is_refused_even_with_the_option()
    {
        var ct = TestContext.Current.CancellationToken;
        var customers = $"ltm_cust_{Suffix()}";
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = customers,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text" }],
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        await CreateOrdersAsync(customers, "null", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_option_value_is_refused()
    {
        await CreateOrdersAsync($"ltm_none_{Suffix()}", "skip", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_client_declares_the_option_and_reads_it_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var customers = $"ltm_cust_{Suffix()}";
        var orders = $"ltm_ord_{Suffix()}";
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        await using var client = new MorphDBClient(http);
        client.SetProjectId(_fixture.Api.ProjectId);

        var table = await client.Schema.CreateTableAsync(new ClientModels.CreateTableRequest
        {
            Name = orders,
            Columns =
            [
                new ClientModels.CreateColumnRequest { Name = "customer_code", Type = "text" },
                new ClientModels.CreateColumnRequest
                {
                    Name = "customer_grade",
                    Type = "text",
                    Lookup = new ClientModels.LookupConfig
                    {
                        RelationColumn = "customer_code", TargetTable = customers, TargetColumn = "grade",
                        MatchColumn = "code", WhenTargetMissing = "null",
                    },
                },
            ],
        }, ct);

        table.Columns.Single(c => c.Name == "customer_grade").Lookup!.WhenTargetMissing.Should().Be("null");

        await CreateCustomersAsync(customers, ("C-1", "gold"));
        var added = await client.Schema.AddColumnAsync(orders, new ClientModels.AddColumnRequest
        {
            Name = "customer_grade_strict",
            Type = "text",
            Lookup = new ClientModels.LookupConfig { RelationColumn = "customer_code", TargetTable = customers, TargetColumn = "grade", MatchColumn = "code" },
        }, ct);
        added.Lookup!.WhenTargetMissing.Should().Be("fail", "the default is named when read back");
    }

    private async Task InsertAsync(string table, object row) =>
        (await _client.PostAsJsonAsync($"/api/data/{table}", row, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

    private async Task<IReadOnlyList<JsonElement>> ListAsync(string tableAndQuery)
    {
        var separator = tableAndQuery.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var response = await _client.GetAsync($"/api/data/{tableAndQuery}{separator}orderBy=customer_code:asc", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data")
            .EnumerateArray().Select(r => r.GetProperty("data").Clone()).ToList();
    }
}
