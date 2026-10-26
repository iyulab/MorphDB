using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The system query options the OData surface answers, and what it does with the rest. A request that names an
/// option the service does not serve — <c>$search</c>, <c>$expand</c>, <c>$apply</c> — is refused, not answered
/// without it: the answer to a narrowing option left out is every row, with a <c>200</c>, and a BI tool that sends
/// <c>$apply</c> for a total gets a table of rows it reads as the total. OData 4.0 Part 1 §6.1 asks a service to
/// fail a request carrying an option it does not understand, and to fail one carrying an unsupported option the
/// specification defines. A custom option (no <c>$</c>) and a parameter alias (<c>@name</c>) are the request's own
/// and are left alone.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataQueryOptionsContractTests
{
    private readonly HttpClient _client;

    public ODataQueryOptionsContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private const int Rows = 12;

    [Theory]
    [InlineData("$search=zzzzz", "$search")]
    [InlineData("$expand=lines", "$expand")]
    [InlineData("$apply=filter(name eq 'n01')", "$apply")]
    [InlineData("$compute=1 as one", "$compute")]
    [InlineData("$skiptoken=abc", "$skiptoken")]
    [InlineData("$schemaversion=1", "$schemaversion")]
    [InlineData("$levels=2", "$levels")]
    [InlineData("$id=x", "$id")]
    [InlineData("$bogus=1", "$bogus")]
    public async Task An_option_the_service_does_not_serve_is_refused_naming_it(string option, string name)
    {
        var set = await SeedAsync();

        var (status, code, message) = await ReadErrorAsync(await _client.GetAsync($"/odata/{set}?{option}", Ct));

        status.Should().Be(HttpStatusCode.BadRequest, option);
        code.Should().Be("INVALID_ARGUMENT");
        message.Should().Contain(name).And.Contain("$filter", "it lists what the service does answer");
    }

    [Theory]
    [InlineData("foo=bar")]
    [InlineData("@p=1")]
    [InlineData("$format=json")]
    [InlineData("$format=application/json;odata.metadata=minimal")]
    public async Task A_custom_option_an_alias_and_json_format_are_not_a_refusal(string option)
    {
        var set = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{set}?{option}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, option);
        (await ValueAsync(response)).Should().HaveCount(Rows);
    }

    [Fact]
    public async Task A_format_the_service_does_not_write_is_refused()
    {
        var set = await SeedAsync();

        var (status, code, message) = await ReadErrorAsync(await _client.GetAsync($"/odata/{set}?$format=xml", Ct));

        status.Should().Be(HttpStatusCode.BadRequest);
        code.Should().Be("INVALID_ARGUMENT");
        message.Should().Contain("$format");
    }

    [Theory]
    [InlineData("$top=-1", "$top")]
    [InlineData("$skip=-1", "$skip")]
    [InlineData("$count=maybe", "$count")]
    public async Task A_paging_or_count_option_that_is_not_a_valid_value_is_refused(string option, string name)
    {
        var set = await SeedAsync();

        var (status, code, _) = await ReadErrorAsync(await _client.GetAsync($"/odata/{set}?{option}", Ct));

        status.Should().Be(HttpStatusCode.BadRequest, name);
        code.Should().Be("INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Top_zero_asks_for_no_rows_and_still_counts()
    {
        var set = await SeedAsync();

        var response = await _client.GetAsync($"/odata/{set}?$top=0&$count=true", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        body.GetProperty("value").GetArrayLength().Should().Be(0, "$top=0 is zero rows, not 'no $top'");
        body.GetProperty("@odata.count").GetInt64().Should().Be(Rows);
        body.TryGetProperty("@odata.nextLink", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_top_beyond_what_one_page_may_carry_is_paged_not_cut_short()
    {
        var set = await SeedAsync(rows: QueryLimits.MaxPageSize + 10);

        var response = await _client.GetAsync($"/odata/{set}?$top={QueryLimits.MaxPageSize + 5}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        var first = body.GetProperty("value").GetArrayLength();
        first.Should().BeLessThan(QueryLimits.MaxPageSize + 5, "one page does not carry what was asked");
        var next = body.GetProperty("@odata.nextLink").GetString()!;
        next.Should().Contain($"$top={QueryLimits.MaxPageSize + 5 - first}", "the rest of what was asked, not the whole ask again");
        next.Should().Contain($"$skip={first}");

        var followed = await _client.GetAsync(new Uri(next).PathAndQuery, Ct);
        (await ValueAsync(followed)).Should().HaveCount(QueryLimits.MaxPageSize + 5 - first);
    }

    [Fact]
    public async Task An_entity_read_takes_select_and_refuses_the_options_of_a_collection()
    {
        var set = await SeedAsync();
        var id = (await ValueAsync(await _client.GetAsync($"/odata/{set}?$top=1", Ct)))[0].GetProperty("_id").GetGuid();

        (await _client.GetAsync($"/odata/{set}({id})?$select=name", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (status, code, message) = await ReadErrorAsync(await _client.GetAsync($"/odata/{set}({id})?$filter=name eq 'n01'", Ct));
        status.Should().Be(HttpStatusCode.BadRequest);
        code.Should().Be("INVALID_ARGUMENT");
        message.Should().Contain("$filter");
    }

    /// <summary>A batch request is answered as the same request on its own: its options are held to the same rules.</summary>
    [Fact]
    public async Task A_batch_request_is_held_to_the_same_options()
    {
        var set = await SeedAsync();

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "a", method = "GET", url = $"{set}?$search=zzz" },
                new { id = "b", method = "GET", url = $"{set}?$top=-1" },
                new { id = "c", method = "GET", url = $"{set}?$top=abc" },
                new { id = "d", method = "GET", url = $"{set}?$top=0" },
            },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("responses")
            .EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);
        items["a"].GetProperty("status").GetInt32().Should().Be(400);
        items["a"].GetProperty("body").GetProperty("error").GetProperty("message").GetString().Should().Contain("$search");
        items["b"].GetProperty("status").GetInt32().Should().Be(400);
        items["c"].GetProperty("status").GetInt32().Should().Be(400, "a $top that is not a number is not 'no $top'");
        items["d"].GetProperty("status").GetInt32().Should().Be(200);
        items["d"].GetProperty("body").GetProperty("value").GetArrayLength().Should().Be(0);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<JsonElement>> ValueAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return [.. JsonDocument.Parse(text).RootElement.GetProperty("value").EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<(HttpStatusCode Status, string Code, string Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        var error = JsonDocument.Parse(text).RootElement.GetProperty("error");
        return (response.StatusCode, error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private async Task<string> SeedAsync(int rows = Rows)
    {
        var table = $"odataopt_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }],
        }, Ct)).EnsureSuccessStatusCode();

        for (var from = 0; from < rows; from += 5000)
        {
            var batch = Enumerable.Range(from, Math.Min(5000, rows - from)).Select(i => new Dictionary<string, object?> { ["name"] = $"n{i:D2}" }).ToArray();
            (await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert", batch, Ct)).EnsureSuccessStatusCode();
        }

        return string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
    }
}
