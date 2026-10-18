using System.Net;
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
/// Every write door goes through the same pipeline, so a value that cannot be read as its column's
/// type is refused the same way through each — by the column's logical name, never PostgreSQL's
/// physical one. A bulk import used to bind such a value as text and carry PostgreSQL's own error,
/// physical column name and all, in the job's <c>errorDetails</c>; and a JSON import handed a date or
/// an id column a string, which PostgreSQL refused although the same row posted singly was written.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class WriteDoorTypeContractTests
{
    private const string Refusal = "Field 'qty' holds integer values; the value given cannot be read as one.";

    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly RowLevelSecurityHarness _rls;

    public WriteDoorTypeContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _rls = new RowLevelSecurityHarness(fixture.Postgres);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IBulkJobExecutor Executor => _fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    private async Task<string> CreateTableAsync()
    {
        var table = $"doortype_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer" },
                new CreateColumnApiRequest { Name = "due", Type = "date" },
                new CreateColumnApiRequest { Name = "ref", Type = "uuid" },
            ],
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        return table;
    }

    /// <summary>Requests an import, carries it out as the job processor does, and returns its status.</summary>
    private async Task<JsonElement> ImportAsync(string table, string format, string contentType, string body)
    {
        var requested = await _client.PostAsync($"/api/bulk/{table}/import/{format}",
            new StringContent(body, Encoding.UTF8, contentType), Ct);
        requested.StatusCode.Should().Be(HttpStatusCode.Accepted, await requested.Content.ReadAsStringAsync(Ct));
        var jobId = JsonDocument.Parse(await requested.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("jobId").GetGuid();

        await As(null, async () =>
        {
            var pending = await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct);
            var job = pending.Should().ContainSingle(j => j.JobId == jobId).Which;
            await _rls.JobProcessor.ProcessImportJobAsync(Executor, job, Ct);
        });

        var status = await _client.GetAsync($"/api/bulk/import/{jobId}", Ct);
        status.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await status.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static string FirstRowError(JsonElement status) =>
        status.GetProperty("errorDetails")[0].GetProperty("error").GetString()!;

    [Fact]
    public async Task A_value_that_is_not_its_columns_type_is_refused_the_same_way_through_every_door()
    {
        var table = await CreateTableAsync();

        var single = await _client.PostAsJsonAsync($"/api/data/{table}", new { code = "s", qty = "many" }, Ct);
        single.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await single.Content.ReadAsStringAsync(Ct)).Should().Contain(Refusal);

        var batch = await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert", new[] { new { code = "b", qty = "many" } }, Ct);
        batch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await batch.Content.ReadAsStringAsync(Ct)).Should().Contain(Refusal);

        var csv = await ImportAsync(table, "csv", "text/csv", "code,qty\nc1,1\nc2,many\n");
        csv.GetProperty("successCount").GetInt64().Should().Be(1);
        FirstRowError(csv).Should().Contain(Refusal).And.NotContain("col_");

        var json = await ImportAsync(table, "json", "application/json", """[{"code":"j1","qty":"many"},{"code":"j2","qty":2}]""");
        json.GetProperty("successCount").GetInt64().Should().Be(1);
        FirstRowError(json).Should().Contain(Refusal).And.NotContain("col_");
    }

    [Fact]
    public async Task A_json_import_writes_date_and_id_values_as_a_single_json_write_does()
    {
        var table = await CreateTableAsync();
        var reference = Guid.NewGuid();

        var status = await ImportAsync(table, "json", "application/json",
            $$"""[{"code":"d1","due":"2026-01-15","ref":"{{reference}}"}]""");

        status.GetProperty("errorCount").GetInt64().Should().Be(0, status.ToString());
        status.GetProperty("successCount").GetInt64().Should().Be(1);

        var rows = await _client.GetAsync($"/api/data/{table}?filter=code:eq:d1", Ct);
        var data = JsonDocument.Parse(await rows.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("data")[0].GetProperty("data");
        data.GetProperty("ref").GetGuid().Should().Be(reference);
        data.GetProperty("due").GetString().Should().StartWith("2026-01-15");
    }
}
