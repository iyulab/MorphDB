using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Controllers;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The <c>column:operator:value</c> filter language, held to what a caller reads it to mean.
/// <para>
/// Every condition applies. The .NET client states several by repeating the <c>filter</c> parameter,
/// and the data endpoint used to bind only the first — a second condition was dropped and the answer
/// came back wider than the question, with a 200. On bulk delete that is more rows gone than asked.
/// </para>
/// <para>
/// Text operators match the value literally: <c>contains:50%</c> finds "50%", not everything
/// containing "50".
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class FilterExpressionContractTests
{
    private readonly HttpClient _client;

    public FilterExpressionContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    [Fact]
    public async Task Repeated_filters_all_apply_on_one_column()
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, "score:gte:20", "score:lt:40");

        names.Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public async Task Repeated_filters_all_apply_across_columns()
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, "score:gte:20", "label:eq:odd", "name:neq:d");

        names.Should().BeEquivalentTo(["c"]);
    }

    [Fact]
    public async Task A_value_may_contain_commas_and_colons()
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, "label:eq:x,y:z");

        names.Should().BeEquivalentTo(["e"]);
    }

    [Theory]
    [InlineData("label:contains:50%", "f")]
    [InlineData("label:contains:a_b", "g")]
    [InlineData("label:startswith:50%", "f")]
    [InlineData("label:endswith:_b", "g")]
    public async Task Text_operators_match_percent_and_underscore_literally(string filter, string expected)
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, filter);

        names.Should().BeEquivalentTo([expected]);
    }

    [Fact]
    public async Task Aggregate_text_filters_match_literally_too()
    {
        var table = await SeedAsync();

        var response = await _client.PostAsJsonAsync($"/api/data/{table}/aggregate", new AggregationApiRequest
        {
            Aggregations = [new AggregationColumnApiRequest { Function = "count", Alias = "n" }],
            Filter = [new QueryFilterConditionApiRequest { Column = "label", Operator = "contains", Value = "a_b" }],
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var result = await response.Content.ReadFromJsonAsync<AggregationApiResponse>(TestContext.Current.CancellationToken);
        Int64(result!.Data[0]["n"]).Should().Be(1, "only 'a_b' contains the literal text; 'axb' would match an unescaped wildcard");
    }

    [Fact]
    public async Task A_malformed_filter_is_refused_not_skipped()
    {
        var table = await SeedAsync();

        var response = await _client.GetAsync($"/api/data/{table}?filter=score:gte:20&filter=nonsense", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bulk_delete_removes_only_the_rows_every_filter_selects()
    {
        var table = await SeedAsync();

        var response = await _client.DeleteAsync($"/api/batch/data/{table}?filter=score:gte:20&filter=score:lt:40", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await QueryNamesAsync(table)).Should().BeEquivalentTo(["a", "d", "e", "f", "g", "h"]);
    }

    [Fact]
    public async Task Bulk_delete_with_a_malformed_filter_deletes_nothing()
    {
        var table = await SeedAsync();

        var response = await _client.DeleteAsync($"/api/batch/data/{table}?filter=score:gte:20&filter=broken", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await QueryNamesAsync(table)).Should().HaveCount(8);
    }

    [Fact]
    public async Task Bulk_update_applies_every_filter()
    {
        var table = await SeedAsync();

        var response = await _client.PatchAsJsonAsync($"/api/batch/data/{table}", new BulkUpdateRequest
        {
            Data = new Dictionary<string, object?> { ["label"] = "touched" },
            Filter = ["score:gte:20", "score:lt:40"],
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await QueryNamesAsync(table, "label:eq:touched")).Should().BeEquivalentTo(["b", "c"],
            "the rows every filter selects get the written value — not the value a filter compared against");
    }

    private async Task<string> SeedAsync()
    {
        var table = $"filter_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "score", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true },
            ],
        }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        (string Name, int? Score, string Label)[] rows =
        [
            ("a", 10, "even"),
            ("b", 20, "even"),
            ("c", 30, "odd"),
            ("d", 40, "odd"),
            ("e", null, "x,y:z"),
            ("f", null, "50% off"),
            ("g", null, "a_b"),
            ("h", null, "axb"),
        ];
        foreach (var (name, score, label) in rows)
        {
            (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["score"] = score,
                ["label"] = label,
            }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }

        return table;
    }

    private async Task<string[]> QueryNamesAsync(string table, params string[] filters)
    {
        var query = string.Concat(filters.Select((f, i) => (i == 0 ? "?" : "&") + "filter=" + Uri.EscapeDataString(f)));
        var response = await _client.GetAsync($"/api/data/{table}{query}{(filters.Length == 0 ? "?" : "&")}pageSize=100", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>(TestContext.Current.CancellationToken);
        return page!.Data.Select(r => r.Data["name"]?.ToString()!).ToArray();
    }

    private static long Int64(object? value) =>
        value is JsonElement element ? element.GetInt64() : Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
