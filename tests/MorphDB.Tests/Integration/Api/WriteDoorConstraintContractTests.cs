using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// «Every write door — data CRUD, batch, seed, upsert, bulk import rows, GraphQL mutations — goes
/// through the same write pipeline: constraint validation (required / unique / FK / CHECK) … and
/// system columns … are applied uniformly.» (docs/API.md, Write Options.) The same rows are sent
/// through each door: one missing a required field, one repeating a unique value, one failing a CHECK
/// — each refused, by the column's logical name and never PostgreSQL's physical name or error code,
/// with nothing written — and one valid row, written with the system columns every door applies.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorConstraintContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly RowLevelSecurityHarness _rls;

    public WriteDoorConstraintContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _rls = new RowLevelSecurityHarness(fixture.Postgres);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Upsert and seed match on the unique key; repeating it updates the row, which is what they are for.
    private static readonly string[] ByCode = ["code"];

    private static readonly string[] ByLabel = ["label"];

    private IBulkJobExecutor Executor => _fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    /// <summary>What a door answered: whether it wrote the row, and everything it said.</summary>
    private sealed record Outcome(bool Written, string Said);

    private delegate Task<Outcome> Door(string table, Dictionary<string, object?> row);

    private async Task<string> CreateTableAsync()
    {
        var table = $"doorcon_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true },
                new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer", Check = "qty >= 0" },
            ],
        }, Ct);
        created.EnsureSuccessStatusCode();
        // The row the duplicate collides with.
        (await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "taken", label = "first", qty = 1 }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private static async Task<Outcome> HttpOutcomeAsync(HttpResponseMessage response)
    {
        var said = await response.Content.ReadAsStringAsync(Ct);
        if (!response.IsSuccessStatusCode)
            return new Outcome(false, said);
        using var body = JsonDocument.Parse(said);
        // Batch-shaped answers report each operation; a refusal there is a failed result.
        if (body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("results", out var results)
            && results.EnumerateArray().Any(r => r.TryGetProperty("success", out var ok) && !ok.GetBoolean()))
            return new Outcome(false, said);
        if (body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("failureCount", out var failures) && failures.GetInt32() > 0)
            return new Outcome(false, said);
        // A seed reports each refused record in `errors`.
        if (body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            return new Outcome(false, said);
        return new Outcome(true, said);
    }

    private async Task<Outcome> ImportAsync(string table, string format, string contentType, string body)
    {
        var requested = await _client.PostAsync($"/api/bulk/{table}/import/{format}", new StringContent(body, Encoding.UTF8, contentType), Ct);
        requested.EnsureSuccessStatusCode();
        var jobId = JsonDocument.Parse(await requested.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("jobId").GetGuid();
        await As(null, async () =>
        {
            var job = (await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == jobId);
            await _rls.JobProcessor.ProcessImportJobAsync(Executor, job, Ct);
        });
        var status = await _client.GetStringAsync($"/api/bulk/import/{jobId}", Ct);
        using var doc = JsonDocument.Parse(status);
        return new Outcome(doc.RootElement.GetProperty("errorCount").GetInt64() == 0, status);
    }

    private static string Csv(Dictionary<string, object?> row)
    {
        var columns = row.Keys.ToList();
        return string.Join(",", columns) + "\n" + string.Join(",", columns.Select(c => Convert.ToString(row[c], System.Globalization.CultureInfo.InvariantCulture))) + "\n";
    }

    private IEnumerable<(string Name, Door Send)> Doors() =>
    [
        ("single insert", async (t, row) => await HttpOutcomeAsync(await _client.PostAsJsonAsync($"/api/data/{t}", row, Ct))),
        ("batch insert", async (t, row) => await HttpOutcomeAsync(await _client.PostAsJsonAsync($"/api/batch/data/{t}/insert", new[] { row }, Ct))),
        ("batch operations", async (t, row) => await HttpOutcomeAsync(await _client.PostAsJsonAsync("/api/batch/data",
            new { operations = new[] { new { method = "INSERT", table = t, data = row } } }, Ct))),
        ("seed", async (t, row) => await HttpOutcomeAsync(await _client.PostAsJsonAsync($"/api/batch/data/{t}/seed",
            new { records = new[] { row }, keyColumns = ByCode }, Ct))),
        ("upsert", async (t, row) => await HttpOutcomeAsync(await _client.PutAsJsonAsync($"/api/batch/data/{t}",
            new { data = row, keyColumns = ByCode }, Ct))),
        ("csv import", (t, row) => ImportAsync(t, "csv", "text/csv", Csv(row))),
        ("json import", (t, row) => ImportAsync(t, "json", "application/json", JsonSerializer.Serialize(new[] { row }))),
        ("graphql", async (t, row) =>
        {
            var response = await _client.PostAsJsonAsync("/graphql", new
            {
                query = "mutation($table: String!, $data: Any!) { createRecord(table: $table, data: $data) { success error errorCode } }",
                variables = new { table = t, data = row },
            }, Ct);
            var said = await response.Content.ReadAsStringAsync(Ct);
            using var doc = JsonDocument.Parse(said);
            var ok = doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                && data.GetProperty("createRecord").GetProperty("success").GetBoolean();
            return new Outcome(ok, said);
        }),
    ];

    private async Task<int> CountAsync(string table)
    {
        using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?pageSize=100", Ct));
        return doc.RootElement.GetProperty("data").GetArrayLength();
    }

    [Fact]
    public async Task A_row_that_breaks_a_constraint_is_refused_the_same_way_through_every_door()
    {
        var violations = new (string What, string Column, Func<string, Dictionary<string, object?>> Row)[]
        {
            ("missing required", "label", door => new() { ["code"] = $"r-{door}", ["qty"] = 1 }),
            ("duplicate unique", "code", door => new() { ["code"] = "taken", ["label"] = $"u-{door}", ["qty"] = 1 }),
            ("failing check", "qty", door => new() { ["code"] = $"c-{door}", ["label"] = $"c-{door}", ["qty"] = -1 }),
        };

        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var table = await CreateTableAsync();
            foreach (var (what, column, row) in violations)
            {
                if (what == "duplicate unique" && door is "seed" or "upsert")
                    continue;
                var outcome = await send(table, row(door.Replace(' ', '_')));
                if (outcome.Written)
                    failures.Add($"{door} / {what}: written — {Trim(outcome.Said)}");
                else if (!outcome.Said.Contains(column, StringComparison.Ordinal) || outcome.Said.Contains("col_", StringComparison.Ordinal))
                    failures.Add($"{door} / {what}: refused without naming '{column}' by its logical name — {Trim(outcome.Said)}");
            }

            var count = await CountAsync(table);
            if (count != 1)
                failures.Add($"{door}: {count - 1} refused row(s) written anyway");
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_valid_row_is_written_with_the_same_system_columns_through_every_door()
    {
        var failures = new List<string>();
        foreach (var (door, send) in Doors())
        {
            var table = await CreateTableAsync();
            var outcome = await send(table, new() { ["code"] = "ok", ["label"] = door, ["qty"] = 2 });
            if (!outcome.Written)
            {
                failures.Add($"{door}: refused a valid row — {Trim(outcome.Said)}");
                continue;
            }

            using var doc = JsonDocument.Parse(await _client.GetStringAsync($"/api/data/{table}?filter=code:eq:ok", Ct));
            var row = doc.RootElement.GetProperty("data")[0];
            var data = row.GetProperty("data");
            foreach (var system in new[] { "_id", "_created_at", "_updated_at", "_version" })
            {
                if (!data.TryGetProperty(system, out var value) || value.ValueKind == JsonValueKind.Null)
                    failures.Add($"{door}: '{system}' not applied");
            }
        }

        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task An_upsert_or_seed_keyed_on_columns_no_unique_constraint_covers_is_the_callers_mistake()
    {
        var table = await CreateTableAsync();
        var row = new Dictionary<string, object?> { ["code"] = "k", ["label"] = "not unique", ["qty"] = 1 };

        var upsert = await _client.PutAsJsonAsync($"/api/batch/data/{table}", new { data = row, keyColumns = ByLabel }, Ct);
        upsert.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        (await upsert.Content.ReadAsStringAsync(Ct)).Should().Contain("no unique constraint covers 'label'");

        var seed = await HttpOutcomeAsync(await _client.PostAsJsonAsync($"/api/batch/data/{table}/seed",
            new { records = new[] { row }, keyColumns = ByLabel }, Ct));
        seed.Written.Should().BeFalse();
        seed.Said.Should().Contain("no unique constraint covers 'label'");
    }

    private static string Trim(string said) => said.Length <= 300 ? said : said[..300] + "…";
}
