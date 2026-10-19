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
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// <c>MorphDB.Client</c> requests an export in each format, reads the job's status as the service
/// carries it out, and downloads the file — and reads an import's status the same way.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkExportClientTests(ApiIntegrationFixture fixture)
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

    private async Task<string> TableWithRowsAsync()
    {
        var table = $"clexport_{Guid.NewGuid():N}"[..30];
        (await fixture.Api.Client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns =
            [
                new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false },
                new CreateColumnApiRequest { Name = "qty", Type = "integer" },
            ],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        foreach (var (code, qty) in new[] { ("a", 1), ("b", 2), ("c", 3) })
        {
            (await fixture.Api.Client.PostAsJsonAsync($"/api/data/{table}",
                new Dictionary<string, object?> { ["code"] = code, ["qty"] = qty }, Ct)).EnsureSuccessStatusCode();
        }

        return table;
    }

    private Task CarryOutExportAsync(Guid jobId) => As(null, async () =>
    {
        var job = (await Executor.GetPendingExportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == jobId);
        await _rls.JobProcessor.ProcessExportJobAsync(Executor, job, Ct);
    });

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, Ct);
            return buffer.ToArray();
        }
    }

    [Fact]
    public async Task A_csv_export_is_requested_carried_out_and_downloaded_with_its_options()
    {
        var table = await TableWithRowsAsync();
        await using var client = NewClient();

        var requested = await client.Bulk.ExportCsvAsync(table, new CsvExportOptions { Columns = ["code", "qty"], Delimiter = ';' }, Ct);
        requested.Status.Should().Be("pending");
        requested.Format.Should().Be("csv");
        requested.TableName.Should().Be(table);

        await CarryOutExportAsync(requested.JobId);

        var done = await client.Bulk.GetExportJobStatusAsync(requested.JobId, Ct);
        done.Should().NotBeNull();
        done!.Status.Should().Be("completed");
        done.TotalRows.Should().Be(3);
        done.FileSize.Should().BePositive();
        done.CompletedAt.Should().NotBeNull();
        done.ExpiresAt.Should().NotBeNull("a stored export file expires");

        var csv = Encoding.UTF8.GetString(await ReadAllAsync(await client.Bulk.DownloadExportAsync(requested.JobId, Ct)));
        // A CSV file opens with a byte-order mark, which tells a spreadsheet the file is UTF-8.
        csv.Should().StartWith("\uFEFF");
        csv.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n')
            .Should().Equal("code;qty", "a;1", "b;2", "c;3");
    }

    [Fact]
    public async Task A_json_and_an_xlsx_export_download_as_their_formats()
    {
        var table = await TableWithRowsAsync();
        await using var client = NewClient();

        var json = await client.Bulk.ExportJsonAsync(table, new JsonExportOptions { Columns = ["code", "qty"] }, Ct);
        var xlsx = await client.Bulk.ExportXlsxAsync(table, new XlsxExportOptions { Columns = ["code"], SheetName = "Codes" }, Ct);
        await CarryOutExportAsync(json.JobId);
        await CarryOutExportAsync(xlsx.JobId);

        using var rows = JsonDocument.Parse(await ReadAllAsync(await client.Bulk.DownloadExportAsync(json.JobId, Ct)));
        rows.RootElement.EnumerateArray().Select(r => r.GetProperty("code").GetString())
            .Should().BeEquivalentTo(["a", "b", "c"]);

        var workbook = await ReadAllAsync(await client.Bulk.DownloadExportAsync(xlsx.JobId, Ct));
        workbook.Take(2).Should().Equal((byte)'P', (byte)'K'); // an xlsx is a zip package
    }

    [Fact]
    public async Task An_unknown_job_reads_as_null_and_an_imports_status_reads_back()
    {
        var table = await TableWithRowsAsync();
        await using var client = NewClient();

        (await client.Bulk.GetExportJobStatusAsync(Guid.NewGuid(), Ct)).Should().BeNull();
        (await client.Bulk.GetImportJobStatusAsync(Guid.NewGuid(), Ct)).Should().BeNull();

        var import = await client.Bulk.ImportCsvAsync(table, new MemoryStream(Encoding.UTF8.GetBytes("code,qty\nd,4\nbad,x\n")), cancellationToken: Ct);
        (await client.Bulk.GetImportJobStatusAsync(import.JobId, Ct))!.Status.Should().Be("pending");

        await As(null, async () =>
        {
            var job = (await Executor.GetPendingImportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == import.JobId);
            await _rls.JobProcessor.ProcessImportJobAsync(Executor, job, Ct);
        });

        var status = await client.Bulk.GetImportJobStatusAsync(import.JobId, Ct);
        status!.TotalRows.Should().Be(2);
        status.SuccessCount.Should().Be(1);
        status.ErrorCount.Should().Be(1);
        status.ErrorDetails.Should().ContainSingle();
    }
}
