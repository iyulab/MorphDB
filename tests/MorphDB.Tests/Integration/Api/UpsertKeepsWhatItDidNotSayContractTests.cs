using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// An upsert that finds its row changes what the request names and leaves the rest: a column the request
/// did not mention keeps the value it had, and the row keeps its place among its siblings. The defaults
/// and the next position an insert is given are for a row that does not exist yet.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class UpsertKeepsWhatItDidNotSayContractTests
{
    private readonly HttpClient _client;

    public UpsertKeepsWhatItDidNotSayContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ByCode = ["code"];

    private delegate Task Door(string table);

    private static async Task Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        var said = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.Should().BeTrue(said);
        said.Should().NotContain("\"success\":false");
    }

    private IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("rest put upsert", t => Ok(_client.PutAsJsonAsync($"/api/batch/data/{t}", new { data = new { code = "k", label = "changed" }, keyColumns = ByCode }, Ct))),
        ("batch operations upsert", t => Ok(_client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = new { code = "k", label = "changed" }, keyColumns = ByCode } } }, Ct))),
        ("seed", t => Ok(_client.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { new { code = "k", label = "changed" } }, keyColumns = ByCode }, Ct))),
        ("graphql upsert", async t =>
        {
            var said = await (await _client.PostAsJsonAsync("/graphql", new
            {
                query = "mutation($table: String!, $data: Any!, $keys: [String!]!) { upsertRecord(table: $table, data: $data, keyColumns: $keys) { success } }",
                variables = new { table = t, data = new { code = "k", label = "changed" }, keys = ByCode },
            }, Ct)).Content.ReadAsStringAsync(Ct);
            said.Should().NotContain("\"errors\"").And.NotContain("\"success\":false");
        }),
    ];

    private async Task<string> CreateAsync()
    {
        var table = $"upskeep_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            SystemColumns = new SystemColumnOptionsApiRequest { Hierarchy = true },
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Default = "0" },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig", qty = 5 }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "other", label = "o", qty = 1 }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private async Task<(long Qty, long Sort, string Label)> ReadAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        return (d.GetProperty("qty").GetInt64(), d.GetProperty("_sort_order").GetInt64(), d.GetProperty("label").GetString()!);
    }

    [Fact]
    public async Task An_upsert_that_finds_its_row_keeps_the_columns_it_did_not_mention_and_the_rows_place()
    {
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var table = await CreateAsync();
            var before = await ReadAsync(table);
            await send(table);
            var after = await ReadAsync(table);

            if (after.Label != "changed")
                failures.Add($"{door}: the change was not written");
            if (after.Qty != before.Qty)
                failures.Add($"{door}: a column the request did not mention went from {before.Qty} to {after.Qty} (its default)");
            if (after.Sort != before.Sort)
                failures.Add($"{door}: _sort_order went from {before.Sort} to {after.Sort} — the row lost its place");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }
}
