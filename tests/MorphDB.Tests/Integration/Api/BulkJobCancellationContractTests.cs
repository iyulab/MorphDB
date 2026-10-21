using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Client;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A cancelled bulk job stays cancelled. Cancelled before the processor reaches it, the job is not
/// started; cancelled while it runs, it stops at its next progress report — an import keeps the rows
/// already written, an export stores no file — and is not marked completed or failed afterwards. The
/// client's cancel reaches the route the service serves. Before, the client posted to routes that do
/// not exist, a job the processor reached was set to processing whatever its state, and one that
/// finished was set to completed whatever had happened to it meanwhile.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkJobCancellationContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly RowLevelSecurityHarness _rls;

    public BulkJobCancellationContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _rls = new RowLevelSecurityHarness(fixture.Postgres);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IBulkJobExecutor Executor => _fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(_fixture.Api.ProjectId);
        return client;
    }

    private async Task<string> CreateTableAsync(int rows)
    {
        var table = $"bulkcancel_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "n", Type = "integer", Nullable = false }],
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        if (rows > 0)
        {
            var seeded = await _client.PostAsJsonAsync($"/api/batch/data/{table}/insert",
                Enumerable.Range(1, rows).Select(n => new Dictionary<string, object?> { ["n"] = n }).ToArray(), Ct);
            seeded.IsSuccessStatusCode.Should().BeTrue(await seeded.Content.ReadAsStringAsync(Ct));
        }

        return table;
    }

    private static string Csv(int rows)
    {
        var csv = new StringBuilder("n\n");
        for (var n = 1; n <= rows; n++)
        {
            csv.Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return csv.ToString();
    }

    private async Task<Guid> RequestImportAsync(string table, int rows)
    {
        var response = await _client.PostAsync($"/api/bulk/{table}/import/csv", new StringContent(Csv(rows), Encoding.UTF8, "text/csv"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("jobId").GetGuid();
    }

    private async Task<Guid> RequestExportAsync(string table)
    {
        var response = await _client.PostAsJsonAsync($"/api/bulk/{table}/export/csv", new CsvExportApiRequest(), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("jobId").GetGuid();
    }

    private async Task<string> StatusAsync(string kind, Guid jobId)
    {
        var response = await _client.GetAsync($"/api/bulk/{kind}/{jobId}", Ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("status").GetString()!;
    }

    private async Task<long> RowCountAsync(string table)
    {
        var response = await _client.GetAsync($"/api/data/{table}?pageSize=1&count=true", Ct);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("pagination").GetProperty("totalCount").GetInt64();
    }

    [Fact]
    public async Task An_export_cancelled_through_the_client_before_it_runs_stays_cancelled_and_stores_no_file()
    {
        var table = await CreateTableAsync(rows: 3);
        var jobId = await RequestExportAsync(table);
        await using var client = NewClient();

        await client.Bulk.CancelExportJobAsync(jobId, Ct);
        var job = (await Executor.GetPendingExportJobsAsync(int.MaxValue, Ct)).SingleOrDefault(j => j.JobId == jobId);

        job.Should().BeNull("a cancelled job is not pending");
        (await StatusAsync("export", jobId)).Should().Be("cancelled");
        (await _client.GetAsync($"/api/bulk/export/{jobId}/download", Ct)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task An_import_cancelled_through_the_client_before_it_runs_writes_nothing()
    {
        var table = await CreateTableAsync(rows: 0);
        var jobId = await RequestImportAsync(table, rows: 5);
        await using var client = NewClient();

        await client.Bulk.CancelImportJobAsync(jobId, Ct);

        (await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct)).Should().NotContain(j => j.JobId == jobId);
        (await StatusAsync("import", jobId)).Should().Be("cancelled");
        (await RowCountAsync(table)).Should().Be(0);
    }

    [Fact]
    public async Task An_export_cancelled_while_it_runs_stops_and_stays_cancelled()
    {
        var table = await CreateTableAsync(rows: 350);
        var jobId = await RequestExportAsync(table);
        var job = (await Executor.GetPendingExportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == jobId);

        // The first bytes the export writes are the moment to cancel it, from another caller.
        using var output = new CancellingStream(() =>
            _client.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, Ct).GetAwaiter().GetResult().StatusCode.Should().Be(HttpStatusCode.NoContent));
        var run = () => As(null, () => Executor.StreamExportAsync(job.JobId, output, Ct));

        await run.Should().ThrowAsync<BulkJobCancelledException>();
        (await StatusAsync("export", jobId)).Should().Be("cancelled");
        await As(null, () => _rls.JobProcessor.ProcessExportJobAsync(Executor, job, Ct));
        (await StatusAsync("export", jobId)).Should().Be("cancelled", "carrying it out again does not revive it");
    }

    [Fact]
    public async Task An_import_cancelled_while_it_runs_keeps_what_it_wrote_and_stays_cancelled()
    {
        var table = await CreateTableAsync(rows: 0);
        var jobId = await RequestImportAsync(table, rows: 350);

        var written = 0;
        var run = () => As(null, async () =>
        {
            await foreach (var _ in Executor.ProcessImportAsync(jobId, Ct))
            {
                if (++written == 1)
                {
                    (await _client.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
                }
            }
        });

        await run.Should().ThrowAsync<BulkJobCancelledException>();
        (await StatusAsync("import", jobId)).Should().Be("cancelled");
        (await RowCountAsync(table)).Should().BeInRange(1, 349, "it stopped at a progress report, keeping the rows before it");
    }

    /// <summary>A stream that runs an action the first time anything is written to it.</summary>
    private sealed class CancellingStream(Action onFirstWrite) : MemoryStream
    {
        private bool _fired;

        public override void Write(byte[] buffer, int offset, int count)
        {
            Fire();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Fire();
            base.Write(buffer);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Fire();
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Fire();
            return base.WriteAsync(buffer, cancellationToken);
        }

        private void Fire()
        {
            if (!_fired)
            {
                _fired = true;
                onFirstWrite();
            }
        }
    }
}
