using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The <c>$filter</c> the OData surface reads. A BI tool states a filter as a combination — <c>and</c>, <c>or</c>,
/// <c>not</c>, parentheses, <c>in</c> — over comparisons, so a surface that reads a single comparison is not one it can be
/// pointed at. What it reads is held here, with each combination's answer worked out from the rows by hand; what it
/// does not read is refused with a message that says what is read, never answered as less than it was asked.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataFilterContractTests
{
    private readonly HttpClient _client;

    public ODataFilterContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // n1..n6 carry qty 1..6, a flag that alternates true/false from n1, and a note on n1..n3; O'Brien has qty 9, flag false and a note.
    [Theory]
    [InlineData("qty ge 2 and qty le 3", "n2,n3")]
    [InlineData("qty eq 1 or qty eq 6", "n1,n6")]
    [InlineData("qty eq 1 or qty ge 4 and flag eq false", "n1,n4,n6,O'Brien")]
    [InlineData("(qty eq 1 or qty ge 4) and flag eq false", "n4,n6,O'Brien")]
    [InlineData("not (qty gt 2)", "n1,n2")]
    [InlineData("not (qty eq 1 or qty eq 2)", "n3,n4,n5,n6,O'Brien")]
    [InlineData("not (qty ge 2 and flag eq true)", "n1,n2,n4,n6,O'Brien")]
    [InlineData("not not qty eq 3", "n3")]
    [InlineData("qty in (1, 3, 5)", "n1,n3,n5")]
    [InlineData("not (qty in (1, 3, 5))", "n2,n4,n6,O'Brien")]
    [InlineData("qty in (1, 2) or qty eq 6", "n1,n2,n6")]
    [InlineData("name in ('n1', 'n2')", "n1,n2")]
    [InlineData("contains(name,'1') or startswith(name,'n6')", "n1,n6")]
    [InlineData("endswith(name,'3') and flag eq true", "n3")]
    [InlineData("name eq 'n1' and not (qty gt 5)", "n1")]
    [InlineData("note eq null", "n4,n5,n6")]
    [InlineData("note ne null and qty gt 1", "n2,n3,O'Brien")]
    [InlineData("qty EQ 2 AND name EQ 'n2'", "n2")]
    [InlineData("flag eq true and ((qty eq 1) or (qty eq 5))", "n1,n5")]
    public async Task A_combination_of_conditions_selects_the_rows_it_says(string filter, string expected)
    {
        var set = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{set}?$filter={Uri.EscapeDataString(filter)}&$orderby=qty", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        Names(text).Should().Equal(expected.Split(','), filter);
    }

    [Fact]
    public async Task A_quote_inside_a_text_is_doubled()
    {
        var set = await SeedAsync();

        var text = await (await _client.GetAsync($"/odata/{set}?$filter={Uri.EscapeDataString("name eq 'O''Brien'")}", Ct)).Content.ReadAsStringAsync(Ct);

        Names(text).Should().Equal("O'Brien");
    }

    [Theory]
    [InlineData("qty eq")]
    [InlineData("(qty eq 1")]
    [InlineData("qty eq 1)")]
    [InlineData("qty eq 1 and")]
    [InlineData("qty eq 1 qty eq 2")]
    [InlineData("qty xx 1")]
    [InlineData("1 eq qty")]
    [InlineData("name eq 'unclosed")]
    [InlineData("qty in ()")]
    [InlineData("qty in (1, null)")]
    [InlineData("qty in 1")]
    [InlineData("not contains(name,'1')")]
    [InlineData("contains(name, 5)")]
    [InlineData("tolower(name) eq 'n1'")]
    [InlineData("qty eq 1 and not")]
    public async Task A_filter_that_is_not_in_the_language_is_refused_and_says_what_is_read(string filter)
    {
        var set = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{set}?$filter={Uri.EscapeDataString(filter)}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, filter);
        var error = JsonDocument.Parse(text).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("VALIDATION_ERROR", filter);
        error.GetProperty("message").GetString().Should().Contain("Supported").And.Contain("and, or, not");
    }

    [Fact]
    public async Task A_filter_inside_a_batch_item_reads_the_same_language()
    {
        var set = await SeedAsync();

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "a", method = "GET", url = $"{set}?$filter=qty in (1, 2) or qty eq 6" },
                new { id = "b", method = "GET", url = $"{set}?$filter=qty eq" },
            },
        }, Ct);

        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("responses")
            .EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);
        items["a"].GetProperty("body").GetProperty("value").GetArrayLength().Should().Be(3);
        items["b"].GetProperty("status").GetInt32().Should().Be(400);
    }

    private static string[] Names(string body) =>
        [.. JsonDocument.Parse(body).RootElement.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("name").GetString()!)];

    private async Task<string> SeedAsync()
    {
        var table = $"odf_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "flag", Type = "boolean", Nullable = true },
                new CreateColumnApiRequest { Name = "note", Type = "text", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();

        var rows = Enumerable.Range(1, 6).Select(i => new Dictionary<string, object?>
        {
            ["name"] = $"n{i}",
            ["qty"] = i,
            ["flag"] = i % 2 == 1,
            ["note"] = i <= 3 ? $"note {i}" : null,
        }).ToList();
        rows.Add(new Dictionary<string, object?> { ["name"] = "O'Brien", ["qty"] = 9, ["flag"] = false, ["note"] = "n/a" });
        (await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert", rows, Ct)).EnsureSuccessStatusCode();
        return string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
    }
}
