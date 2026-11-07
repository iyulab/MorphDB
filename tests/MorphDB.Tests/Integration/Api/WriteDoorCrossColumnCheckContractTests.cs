using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A CHECK that names two columns (<c>hi &gt;= lo</c>) holds the row, not the request: a door that changes only
/// <c>hi</c> has to compare it with the <c>lo</c> the row already stores. The CHECK is evaluated by the service from
/// the values it is handed, and the column the request did not name was read from the request alone — absent there, so
/// the comparison had nothing to compare and the CHECK passed. The same change (<c>hi</c> to a value below the stored
/// <c>lo</c>) is sent through each door that updates a row; a door that accepts it has stored a row its own table says
/// cannot exist. A change that keeps the CHECK true goes through every door, to show the refusal is the CHECK and not
/// the door.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorCrossColumnCheckContractTests
{
    private readonly HttpClient _client;

    public WriteDoorCrossColumnCheckContractTests(ApiIntegrationFixture fixture) => _client = fixture.Api.Client;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ByCode = ["code"];

    private static readonly string[] CodeIsK = ["code:eq:k"];

    private static readonly string[] LoAtLeastFive = ["lo:gte:5"];

    private delegate Task Door(string table, Guid id, string field, int value);

    /// <summary>A row with lo = 5 and hi = 10, under the CHECK <c>hi &gt;= lo</c>.</summary>
    private async Task<(string Table, Guid Id)> RowAsync()
    {
        var table = $"doorchk_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "lo", Type = "integer", Nullable = true },
                new CreateColumnApiRequest { Name = "hi", Type = "integer", Nullable = true, Check = "hi >= lo" },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "k", lo = 5, hi = 10 }, Ct)).EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        return (table, doc.RootElement.GetProperty("data")[0].GetProperty("data").GetProperty("_id").GetGuid());
    }

    private async Task<(long Lo, long Hi)> ReadAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:k", Ct));
        var data = doc.RootElement.GetProperty("data")[0].GetProperty("data");
        return (data.GetProperty("lo").GetInt64(), data.GetProperty("hi").GetInt64());
    }

    private async Task GraphQlAsync(string query, object variables) =>
        await (await _client.PostAsJsonAsync("/graphql", new { query, variables }, Ct)).Content.ReadAsStringAsync(Ct);

    private static Dictionary<string, object> One(string field, int value) => new() { [field] = value };

    private static Dictionary<string, object> Keyed(string field, int value) => new() { ["code"] = "k", [field] = value };

    private IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("rest patch", async (t, id, f, v) => await _client.PatchAsJsonAsync($"/api/data/{t}/{id}", One(f, v), Ct)),
        ("rest put upsert", async (t, id, f, v) => await _client.PutAsJsonAsync($"/api/batch/data/{t}",
            new { data = Keyed(f, v), keyColumns = ByCode }, Ct)),
        ("batch operations update", async (t, id, f, v) => await _client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPDATE", table = t, id, data = One(f, v) } } }, Ct)),
        ("batch operations upsert", async (t, id, f, v) => await _client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "UPSERT", table = t, data = Keyed(f, v), keyColumns = ByCode } } }, Ct)),
        ("seed", async (t, id, f, v) => await _client.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { Keyed(f, v) }, keyColumns = ByCode }, Ct)),
        ("update by filter", async (t, id, f, v) => await _client.PatchAsJsonAsync($"/api/batch/data/{t}",
            new { data = One(f, v), filter = CodeIsK }, Ct)),
        ("graphql update", (t, id, f, v) => GraphQlAsync(
            "mutation($table: String!, $id: UUID!, $data: Any!) { updateRecord(table: $table, id: $id, data: $data) { success } }",
            new { table = t, id, data = One(f, v) })),
        ("graphql upsert", (t, id, f, v) => GraphQlAsync(
            "mutation($table: String!, $data: Any!, $keys: [String!]!) { upsertRecord(table: $table, data: $data, keyColumns: $keys) { success } }",
            new { table = t, data = Keyed(f, v), keys = ByCode })),
        ("odata patch", async (t, id, f, v) =>
        {
            var set = string.Concat(t.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant() : p));
            await _client.PatchAsJsonAsync($"/odata/{set}({id})", One(f, v), Ct);
        }),
    ];

    [Fact]
    public async Task A_door_that_changes_one_column_of_a_two_column_check_compares_it_with_the_stored_other()
    {
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var (table, id) = await RowAsync();
            await send(table, id, "hi", 3);
            var (lo, hi) = await ReadAsync(table);
            if (hi < lo)
                failures.Add($"{door}: stored lo = {lo}, hi = {hi}, which the CHECK 'hi >= lo' forbids");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_change_that_keeps_the_check_true_goes_through_every_door()
    {
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var (table, id) = await RowAsync();
            await send(table, id, "hi", 7);
            var (_, hi) = await ReadAsync(table);
            if (hi != 7)
                failures.Add($"{door}: hi stayed {hi}, a value the CHECK allows was refused");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_door_that_changes_the_other_column_is_held_by_the_check_too()
    {
        // The CHECK sits on `hi`, and the change is to `lo`: lo 5 -> 20 against the stored hi of 10.
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var (table, id) = await RowAsync();
            await send(table, id, "lo", 20);
            var (lo, hi) = await ReadAsync(table);
            if (hi < lo)
                failures.Add($"{door}: stored lo = {lo}, hi = {hi}, which the CHECK 'hi >= lo' forbids");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task An_update_by_filter_that_would_break_the_check_on_one_row_changes_none()
    {
        var (table, _) = await RowAsync();
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "j", lo = 8, hi = 9 }, Ct)).EnsureSuccessStatusCode();

        // hi = 7 holds for the row with lo 5 and breaks it for the row with lo 8.
        using var both = await _client.PatchAsJsonAsync($"/api/batch/data/{table}",
            new { data = new { hi = 7 }, filter = LoAtLeastFive }, Ct);
        both.IsSuccessStatusCode.Should().BeFalse("one of the two rows would be left under its lo");
        (await ReadAsync(table)).Hi.Should().Be(10, "a statement refused for one row changes no row");

        using var one = await _client.PatchAsJsonAsync($"/api/batch/data/{table}",
            new { data = new { hi = 7 }, filter = CodeIsK }, Ct);
        one.EnsureSuccessStatusCode();
        (await ReadAsync(table)).Hi.Should().Be(7);
    }
}
