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

    /// <summary>
    /// <c>in</c> and <c>notin</c> read their values as a JSON array, so a value may hold a comma; a list
    /// written any other way is refused rather than read as one value.
    /// </summary>
    [Theory]
    [InlineData("name:in:[\"a\",\"c\",\"zz\"]", new[] { "a", "c" })]
    [InlineData("score:in:[10,40]", new[] { "a", "d" })]
    [InlineData("label:in:[\"x,y:z\"]", new[] { "e" })]
    [InlineData("name:in:[]", new string[0])]
    public async Task In_selects_the_rows_whose_value_is_listed(string filter, string[] expected)
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, filter);

        names.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task Notin_combines_with_other_filters()
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, "label:notin:[\"even\",\"odd\"]", "score:isnull");

        names.Should().BeEquivalentTo(["e", "f", "g", "h"]);
    }

    [Theory]
    [InlineData("name:in:a,c")]
    [InlineData("name:in:\"a\"")]
    [InlineData("name:in:[null]")]
    [InlineData("name:in:[[\"a\"]]")]
    public async Task A_list_not_written_as_a_json_array_of_values_is_refused(string filter)
    {
        var table = await SeedAsync();

        var response = await _client.GetAsync($"/api/data/{table}?filter={Uri.EscapeDataString(filter)}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_query_body_takes_in_with_a_list_inside_an_or_group()
    {
        var table = await SeedAsync();

        var response = await _client.PostAsync($"/api/data/{table}/query", new StringContent(
            """{"filter":{"$type":"group","logic":"or","filters":[{"$type":"condition","column":"name","operator":"in","value":["a","b"]},{"$type":"condition","column":"label","operator":"eq","value":"a_b"}]},"pageSize":100}""",
            System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var page = await response.Content.ReadFromJsonAsync<PagedResponse<DataRecordResponse>>(TestContext.Current.CancellationToken);
        page!.Data.Select(r => r.Data["name"]?.ToString()).Should().BeEquivalentTo(["a", "b", "g"]);
    }

    [Fact]
    public async Task A_malformed_filter_is_refused_not_skipped()
    {
        var table = await SeedAsync();

        var response = await _client.GetAsync($"/api/data/{table}?filter=score:gte:20&filter=nonsense", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An order direction other than <c>asc</c>/<c>desc</c> used to read as ascending — the list came
    /// back in an order the caller did not ask for, with a 200.
    /// </summary>
    [Theory]
    [InlineData("GET", "?orderBy=score:descending")]
    [InlineData("POST query", """{"orderBy":["score:up"]}""")]
    [InlineData("POST aggregate", """{"aggregations":[{"function":"count","alias":"n"}],"groupBy":["name"],"orderBy":[{"column":"name","direction":"down"}]}""")]
    public async Task An_order_direction_it_cannot_read_is_refused(string route, string request)
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await SeedAsync();

        var response = route switch
        {
            "GET" => await _client.GetAsync($"/api/data/{table}{request}", ct),
            "POST query" => await _client.PostAsync($"/api/data/{table}/query", new StringContent(request, System.Text.Encoding.UTF8, "application/json"), ct),
            _ => await _client.PostAsync($"/api/data/{table}/aggregate", new StringContent(request, System.Text.Encoding.UTF8, "application/json"), ct),
        };

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(ct));
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("the direction is 'asc' or 'desc'");
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

    [Theory]
    [InlineData("score:isnull:")]
    [InlineData("score:isnull")]
    public async Task Isnull_selects_the_rows_whose_column_is_empty(string filter)
    {
        var table = await SeedAsync();

        var names = await QueryNamesAsync(table, filter);

        names.Should().BeEquivalentTo(["e", "f", "g", "h"]);
    }

    [Fact]
    public async Task Isnotnull_selects_the_rows_whose_column_has_a_value_and_combines_with_others()
    {
        var table = await SeedAsync();

        (await QueryNamesAsync(table, "score:isnotnull:")).Should().BeEquivalentTo(["a", "b", "c", "d"]);
        (await QueryNamesAsync(table, "score:isnotnull:", "label:eq:odd")).Should().BeEquivalentTo(["c", "d"]);
    }

    [Fact]
    public async Task A_value_on_a_valueless_operator_is_refused()
    {
        var table = await SeedAsync();

        var response = await _client.GetAsync($"/api/data/{table}?filter=score:isnull:0", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "'is null, and also 0' has no reading — dropping the value would answer a different question");
    }

    [Fact]
    public async Task Bulk_delete_by_isnull_removes_only_the_empty_rows()
    {
        var table = await SeedAsync();

        var response = await _client.DeleteAsync($"/api/batch/data/{table}?filter=score:isnull:", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await QueryNamesAsync(table)).Should().BeEquivalentTo(["a", "b", "c", "d"]);
    }

    [Fact]
    public async Task Aggregate_counts_by_isnull()
    {
        var table = await SeedAsync();

        var response = await _client.PostAsJsonAsync($"/api/data/{table}/aggregate", new AggregationApiRequest
        {
            Aggregations = [new AggregationColumnApiRequest { Function = "count", Alias = "n" }],
            Filter = [new QueryFilterConditionApiRequest { Column = "score", Operator = "isnull" }],
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var result = await response.Content.ReadFromJsonAsync<AggregationApiResponse>(TestContext.Current.CancellationToken);
        Int64(result!.Data[0]["n"]).Should().Be(4);
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
