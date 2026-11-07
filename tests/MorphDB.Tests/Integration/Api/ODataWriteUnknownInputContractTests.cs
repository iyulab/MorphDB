using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The OData doors that change a row answer only the input they take, as the REST doors do
/// (<c>WriteDoorUnknownInputContractTests</c>). A system query option is refused on a write — the write takes none,
/// and <c>$filter</c> on a DELETE would otherwise read as «delete what matches» and delete the one entity the key
/// names regardless — and so is a conditional header, which no door evaluates. A custom option (no <c>$</c>) belongs
/// to the request and is left alone, as it is on a read.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ODataWriteUnknownInputContractTests
{
    private readonly HttpClient _client;

    public ODataWriteUnknownInputContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(string Set, string Table, Guid Id)> TableWithOneRowAsync()
    {
        var table = $"odatain_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "v1" }, Ct)).EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var id = doc.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("_id").GetGuid();
        var set = string.Concat(table.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
        return (set, table, id);
    }

    private async Task<string> SnapshotAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?pageSize=100", Ct));
        var rows = doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("data"))
            .Select(d => $"{d.GetProperty("code").GetString()}/{d.GetProperty("label").GetString()}/v{d.GetProperty("_version").GetInt64()}")
            .Order();
        return string.Join(" ", rows);
    }

    private static IEnumerable<(string Name, Func<string, Guid, string, HttpRequestMessage> Build)> Doors() =>
    [
        ("odata post", (set, _, q) => new HttpRequestMessage(HttpMethod.Post, $"/odata/{set}{q}") { Content = JsonContent.Create(new { code = "new", label = "n" }) }),
        ("odata patch", (set, id, q) => new HttpRequestMessage(HttpMethod.Patch, $"/odata/{set}({id}){q}") { Content = JsonContent.Create(new { label = "changed" }) }),
        ("odata delete", (set, id, q) => new HttpRequestMessage(HttpMethod.Delete, $"/odata/{set}({id}){q}")),
    ];

    [Theory]
    [InlineData("?$filter=code eq 'zz'", "$filter")]
    [InlineData("?$select=label", "$select")]
    [InlineData("?$top=1", "$top")]
    public async Task An_odata_write_refuses_a_system_option_it_does_not_serve_and_writes_nothing(string query, string option)
    {
        var failures = new List<string>();
        foreach (var (door, build) in Doors())
        {
            var (set, table, id) = await TableWithOneRowAsync();
            var before = await SnapshotAsync(table);

            using var response = await _client.SendAsync(build(set, id, query), Ct);
            var said = await response.Content.ReadAsStringAsync(Ct);

            if (response.StatusCode != HttpStatusCode.BadRequest)
                failures.Add($"{door}: answered {(int)response.StatusCode}, expected 400 — {Trim(said)}");
            else if (!said.Contains(option, StringComparison.Ordinal))
                failures.Add($"{door}: the refusal does not name the option — {Trim(said)}");

            var after = await SnapshotAsync(table);
            if (after != before)
                failures.Add($"{door}: the table changed anyway ({before} -> {after})");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Theory]
    [InlineData("If-Match", "\"1\"")]
    [InlineData("If-None-Match", "*")]
    [InlineData("If-Unmodified-Since", "Sat, 07 Nov 2026 00:00:00 GMT")]
    public async Task An_odata_write_refuses_a_conditional_header_it_cannot_honor_and_writes_nothing(string header, string value)
    {
        var failures = new List<string>();
        foreach (var (door, build) in Doors())
        {
            var (set, table, id) = await TableWithOneRowAsync();
            var before = await SnapshotAsync(table);

            using var request = build(set, id, "");
            request.Headers.TryAddWithoutValidation(header, value);
            using var response = await _client.SendAsync(request, Ct);
            var said = await response.Content.ReadAsStringAsync(Ct);

            if (response.StatusCode != HttpStatusCode.BadRequest)
                failures.Add($"{door}: answered {(int)response.StatusCode}, expected 400 — {Trim(said)}");
            else if (!said.Contains(header, StringComparison.OrdinalIgnoreCase))
                failures.Add($"{door}: the refusal does not name the header — {Trim(said)}");

            var after = await SnapshotAsync(table);
            if (after != before)
                failures.Add($"{door}: the table changed anyway ({before} -> {after})");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_custom_option_and_the_format_option_are_the_requests_own_and_pass()
    {
        var (set, table, id) = await TableWithOneRowAsync();

        using var patch = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/odata/{set}({id})?note=1&$format=json")
        {
            Content = JsonContent.Create(new { label = "changed" }),
        }, Ct);
        patch.EnsureSuccessStatusCode();

        (await SnapshotAsync(table)).Should().Be("k/changed/v2");
    }

    [Fact]
    public async Task A_batch_item_that_writes_is_held_to_the_same_options()
    {
        var (set, table, id) = await TableWithOneRowAsync();
        var before = await SnapshotAsync(table);

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "a", method = "PATCH", url = $"{set}({id})?$filter=code eq 'zz'", body = new { label = "changed" } },
                new { id = "b", method = "DELETE", url = $"{set}({id})?$top=1" },
                new { id = "c", method = "POST", url = $"{set}?$select=label", body = new { code = "new", label = "n" } },
            },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("responses")
            .EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!);
        foreach (var item in new[] { "a", "b", "c" })
            items[item].GetProperty("status").GetInt32().Should().Be(400, $"item {item} carries an option a write does not take");
        (await SnapshotAsync(table)).Should().Be(before);
    }

    [Fact]
    public async Task A_batch_request_that_writes_with_a_conditional_header_is_refused_whole()
    {
        var (set, table, id) = await TableWithOneRowAsync();
        var before = await SnapshotAsync(table);

        var response = await _client.PostAsJsonAsync("/odata/$batch", new
        {
            requests = new object[]
            {
                new { id = "a", method = "GET", url = $"{set}", headers = new Dictionary<string, string> { ["If-None-Match"] = "\"x\"" } },
                new { id = "b", method = "PATCH", url = $"{set}({id})", headers = new Dictionary<string, string> { ["if-match"] = "\"1\"" }, body = new { label = "changed" } },
            },
        }, Ct);

        var said = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, said);
        said.Should().Contain("if-match", "the refusal names the header it found").And.Contain("'b'");
        (await SnapshotAsync(table)).Should().Be(before, "a batch refused whole runs none of its requests");
    }

    private static string Trim(string said) => said.Length <= 200 ? said : said[..200] + "…";
}
