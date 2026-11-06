using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// «Every write door … applies the system columns uniformly.» (docs/API.md, Write Options.) A door
/// that changes an existing row must raise its <c>_version</c> by one, move <c>_updated_at</c> forward
/// and leave <c>_id</c> and <c>_created_at</c> alone — the row's optimistic-lock version is only
/// worth reading if no door sends it backwards or leaves it behind. The same change is made to a row
/// at version 2 through each door that updates a row, and the system columns are read back.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorSystemColumnContractTests
{
    private readonly HttpClient _client;

    public WriteDoorSystemColumnContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ByCode = ["code"];

    private static readonly string[] CodeIsK = ["code:eq:k"];

    private sealed record Row(Guid Id, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Label);

    private delegate Task Door(string table, Guid id);

    private async Task<string> CreateTableAsync()
    {
        var table = $"doorver_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private async Task<Row> ReadAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var data = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        return new Row(
            data.GetProperty("_id").GetGuid(),
            data.GetProperty("_version").GetInt64(),
            data.GetProperty("_created_at").GetDateTimeOffset(),
            data.GetProperty("_updated_at").GetDateTimeOffset(),
            data.GetProperty("label").GetString()!);
    }

    /// <summary>A row at version 2: created, then changed once through the plain update.</summary>
    private async Task<(string Table, Row Before)> RowAtVersionTwoAsync()
    {
        var table = await CreateTableAsync();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", label = "v1" }, Ct)).EnsureSuccessStatusCode();
        var created = await ReadAsync(table);
        (await _client.PatchAsJsonAsync($"/api/data/{table}/{created.Id}", new { label = "v2" }, Ct)).EnsureSuccessStatusCode();
        var before = await ReadAsync(table);
        before.Version.Should().Be(2, "the starting row must be at version 2 for a reset to 1 to show");
        return (table, before);
    }

    private async Task<HttpResponseMessage> Check(Task<HttpResponseMessage> call)
    {
        var response = await call;
        (await response.Content.ReadAsStringAsync(Ct)).Should().NotContain("\"success\":false");
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task GraphQlAsync(string query, object variables)
    {
        var response = await _client.PostAsJsonAsync("/graphql", new { query, variables }, Ct);
        var said = await response.Content.ReadAsStringAsync(Ct);
        said.Should().NotContain("\"errors\"").And.NotContain("\"success\":false");
    }

    private IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("rest patch", async (t, id) => await Check(_client.PatchAsJsonAsync($"/api/data/{t}/{id}", new { label = "changed" }, Ct))),
        ("rest put upsert", async (t, id) => await Check(_client.PutAsJsonAsync($"/api/batch/data/{t}",
            new { data = new { code = "k", label = "changed" }, keyColumns = ByCode }, Ct))),
        ("batch operations update", async (t, id) => await Check(_client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPDATE", table = t, id, data = new { label = "changed" } } } }, Ct))),
        ("batch operations upsert", async (t, id) => await Check(_client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = new { code = "k", label = "changed" }, keyColumns = ByCode } } }, Ct))),
        ("seed", async (t, id) => await Check(_client.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { new { code = "k", label = "changed" } }, keyColumns = ByCode }, Ct))),
        ("update by filter", async (t, id) => await Check(_client.PatchAsJsonAsync($"/api/batch/data/{t}",
            new { data = new { label = "changed" }, filter = CodeIsK }, Ct))),
        ("graphql update", (t, id) => GraphQlAsync(
            "mutation($table: String!, $id: UUID!, $data: Any!) { updateRecord(table: $table, id: $id, data: $data) { success } }",
            new { table = t, id, data = new { label = "changed" } })),
        ("graphql upsert", (t, id) => GraphQlAsync(
            "mutation($table: String!, $data: Any!, $keys: [String!]!) { upsertRecord(table: $table, data: $data, keyColumns: $keys) { success } }",
            new { table = t, data = new { code = "k", label = "changed" }, keys = ByCode })),
        ("odata patch", async (t, id) =>
        {
            var set = string.Concat(t.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
            await Check(_client.PatchAsJsonAsync($"/odata/{set}({id})", new { label = "changed" }, Ct));
        }),
    ];

    [Fact]
    public async Task A_door_that_changes_an_existing_row_raises_its_version_by_one_and_keeps_its_identity()
    {
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var (table, before) = await RowAtVersionTwoAsync();
            await Task.Delay(20, Ct);
            await send(table, before.Id);
            var after = await ReadAsync(table);

            if (after.Label != "changed")
                failures.Add($"{door}: the change was not written (label '{after.Label}')");
            if (after.Version != before.Version + 1)
                failures.Add($"{door}: _version {before.Version} -> {after.Version}, expected {before.Version + 1}");
            if (after.UpdatedAt <= before.UpdatedAt)
                failures.Add($"{door}: _updated_at did not move ({before.UpdatedAt:O} -> {after.UpdatedAt:O})");
            if (after.CreatedAt != before.CreatedAt)
                failures.Add($"{door}: _created_at changed");
            if (after.Id != before.Id)
                failures.Add($"{door}: _id changed");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }
}
