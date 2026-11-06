using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// «Every write door … goes through the same write pipeline: constraint validation (required / unique /
/// FK / CHECK) … are applied uniformly.» (docs/API.md, Write Options.) <see cref="WriteDoorConstraintContractTests"/>
/// sends rows that break a constraint into each door that creates a row; this sends a change that breaks
/// one into each door that changes an existing row — the same row, the same three breaks (a required
/// column set to null, a unique value another row holds, a failing CHECK). Each is refused, by the
/// column's logical name and never the physical name, and the row is left as it was.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorUpdateConstraintContractTests
{
    private readonly HttpClient _client;

    public WriteDoorUpdateConstraintContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ByCode = ["code"];

    private static readonly string[] CodeIsK = ["code:eq:k"];

    private sealed record Outcome(bool Ok, string Said);

    private delegate Task<Outcome> Door(string table, Guid id, Dictionary<string, object?> change, bool keyed);

    private async Task<(string Table, Guid Id)> CreateAsync()
    {
        var table = $"doorupd_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Check = "qty >= 0" },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "taken", label = "other", qty = 1 }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "orig", qty = 5 }, Ct)).EnsureSuccessStatusCode();
        return (table, (await ReadAsync(table)).Id);
    }

    private sealed record Row(Guid Id, string Code, string Label, long Qty, long Version);

    private async Task<Row> ReadAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var d = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        return new Row(d.GetProperty("_id").GetGuid(), d.GetProperty("code").GetString()!,
            d.GetProperty("label").GetString()!, d.GetProperty("qty").GetInt64(), d.GetProperty("_version").GetInt64());
    }

    private static async Task<Outcome> HttpAsync(HttpResponseMessage response)
    {
        var said = await response.Content.ReadAsStringAsync(Ct);
        if (!response.IsSuccessStatusCode)
            return new Outcome(false, said);
        using var body = JsonDocument.Parse(said);
        if (body.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (body.RootElement.TryGetProperty("results", out var results)
                && results.EnumerateArray().Any(r => r.TryGetProperty("success", out var ok) && !ok.GetBoolean()))
                return new Outcome(false, said);
            if (body.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                return new Outcome(false, said);
            if (body.RootElement.TryGetProperty("failureCount", out var failures) && failures.GetInt32() > 0)
                return new Outcome(false, said);
        }

        return new Outcome(true, said);
    }

    private async Task<Outcome> GraphQlAsync(string query, object variables)
    {
        var said = await (await _client.PostAsJsonAsync("/graphql", new { query, variables }, Ct)).Content.ReadAsStringAsync(Ct);
        using var doc = JsonDocument.Parse(said);
        var ok = !said.Contains("\"errors\"", StringComparison.Ordinal) && !said.Contains("\"success\":false", StringComparison.Ordinal)
            && doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
        return new Outcome(ok, said);
    }

    private static Dictionary<string, object?> WithKey(Dictionary<string, object?> change) =>
        new(change) { ["code"] = change.GetValueOrDefault("code") ?? "k" };

    // `keyed` doors match on the unique code, so a break of that same column cannot be sent through them.
    private IEnumerable<(string Name, bool Keyed, Door Send)> Doors() =>
    [
        ("rest patch", false, async (t, id, c, _) => await HttpAsync(await _client.PatchAsJsonAsync($"/api/data/{t}/{id}", c, Ct))),
        ("batch operations update", false, async (t, id, c, _) => await HttpAsync(await _client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPDATE", table = t, id, data = c } } }, Ct))),
        ("update by filter", false, async (t, id, c, _) => await HttpAsync(await _client.PatchAsJsonAsync($"/api/batch/data/{t}",
            new { data = c, filter = CodeIsK }, Ct))),
        ("graphql update", false, (t, id, c, _) => GraphQlAsync(
            "mutation($table: String!, $id: UUID!, $data: Any!) { updateRecord(table: $table, id: $id, data: $data) { success error } }",
            new { table = t, id, data = c })),
        ("odata patch", false, async (t, id, c, _) =>
        {
            var set = string.Concat(t.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
            return await HttpAsync(await _client.PatchAsJsonAsync($"/odata/{set}({id})", c, Ct));
        }),
        ("rest put upsert", true, async (t, id, c, _) => await HttpAsync(await _client.PutAsJsonAsync($"/api/batch/data/{t}",
            new { data = WithKey(c), keyColumns = ByCode }, Ct))),
        ("batch operations upsert", true, async (t, id, c, _) => await HttpAsync(await _client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = WithKey(c), keyColumns = ByCode } } }, Ct))),
        ("seed", true, async (t, id, c, _) => await HttpAsync(await _client.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { WithKey(c) }, keyColumns = ByCode }, Ct))),
        ("graphql upsert", true, (t, id, c, _) => GraphQlAsync(
            "mutation($table: String!, $data: Any!, $keys: [String!]!) { upsertRecord(table: $table, data: $data, keyColumns: $keys) { success error } }",
            new { table = t, data = WithKey(c), keys = ByCode })),
    ];

    [Fact]
    public async Task A_change_that_breaks_a_constraint_is_refused_the_same_way_through_every_door_and_leaves_the_row_alone()
    {
        var breaks = new (string What, string Column, bool NeedsFreeKey, Dictionary<string, object?> Change)[]
        {
            ("required column set to null", "label", false, new() { ["label"] = null }),
            ("unique value another row holds", "code", true, new() { ["code"] = "taken" }),
            ("failing check", "qty", false, new() { ["qty"] = -1 }),
        };

        var failures = new List<string>();
        foreach (var (door, keyed, send) in Doors())
        {
            foreach (var (what, column, needsFreeKey, change) in breaks)
            {
                if (needsFreeKey && keyed)
                    continue;
                var (table, id) = await CreateAsync();
                var before = await ReadAsync(table);
                var outcome = await send(table, id, new Dictionary<string, object?>(change), keyed);
                var after = await ReadAsync(table);

                if (outcome.Ok)
                    failures.Add($"{door} / {what}: answered as written — {Trim(outcome.Said)}");
                else if (!outcome.Said.Contains(column, StringComparison.Ordinal) || outcome.Said.Contains("col_", StringComparison.Ordinal))
                    failures.Add($"{door} / {what}: refused without naming '{column}' by its logical name — {Trim(outcome.Said)}");

                if (after != before)
                    failures.Add($"{door} / {what}: the row changed anyway — {before} -> {after}");
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    private static string Trim(string said) => said.Length <= 300 ? said : said[..300] + "…";
}
