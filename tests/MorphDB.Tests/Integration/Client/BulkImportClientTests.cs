using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using MorphDB.Tests.Integration;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// <c>MorphDB.Client</c> imports a file the way the service reads one: the file as the request body,
/// under its format's media type, and the options in the query. The client sent a multipart form,
/// which the import routes refuse, so no import through the client could be requested at all.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkImportClientTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RowLevelSecurityHarness _rls = new(fixture.Postgres);

    private IBulkJobExecutor Executor => fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    private async Task<string> CreateTableAsync()
    {
        var table = $"climport_{Guid.NewGuid():N}"[..30];
        (await fixture.Api.Client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer" },
            ],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        return table;
    }

    /// <summary>Carries the job out as the processor does, then reads what it wrote.</summary>
    private async Task<IReadOnlyList<(string Code, int Qty)>> CarryOutAndReadAsync(string table, Guid jobId)
    {
        await As(null, async () =>
        {
            var job = (await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == jobId);
            await _rls.JobProcessor.ProcessImportJobAsync(Executor, job, Ct);
        });

        var response = await fixture.Api.Client.GetAsync($"/api/data/{table}?orderBy=code:asc", Ct);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return [.. body.RootElement.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("data")).Select(d =>
            (d.GetProperty("code").GetString()!, d.GetProperty("qty").GetInt32()))];
    }

    private static MemoryStream File(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task A_csv_import_through_the_client_is_requested_and_carried_out_with_its_options()
    {
        var table = await CreateTableAsync();
        await using var client = NewClient();

        var job = await client.Bulk.ImportCsvAsync(table, File("code;qty\na;1\nb;2\n"), new CsvImportOptions { Delimiter = ';' }, Ct);

        (await CarryOutAndReadAsync(table, job.JobId)).Should().Equal(("a", 1), ("b", 2));
    }

    [Fact]
    public async Task A_json_import_through_the_client_is_requested_and_carried_out()
    {
        var table = await CreateTableAsync();
        await using var client = NewClient();

        var job = await client.Bulk.ImportJsonAsync(table, File("""[{"code":"a","qty":1},{"code":"b","qty":2}]"""), cancellationToken: Ct);

        (await CarryOutAndReadAsync(table, job.JobId)).Should().Equal(("a", 1), ("b", 2));
    }

    [Fact]
    public async Task An_ndjson_import_through_the_client_is_requested_and_carried_out()
    {
        var table = await CreateTableAsync();
        await using var client = NewClient();

        var lines = string.Join("\n", new[] { ("a", 1), ("b", 2) }.Select(r =>
            string.Create(CultureInfo.InvariantCulture, $$"""{"code":"{{r.Item1}}","qty":{{r.Item2}}}""")));
        var job = await client.Bulk.ImportNdjsonAsync(table, File(lines), cancellationToken: Ct);

        (await CarryOutAndReadAsync(table, job.JobId)).Should().Equal(("a", 1), ("b", 2));
    }
}
