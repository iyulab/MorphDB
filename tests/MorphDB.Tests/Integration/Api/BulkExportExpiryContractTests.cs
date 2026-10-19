using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// An export's file is kept until the job's <c>expiresAt</c>. After it, the download answers
/// <c>410 EXPORT_EXPIRED</c> and the job processor removes the stored copy — a copy of rows that would
/// otherwise outlive every later change to them. The expiry was recorded on every job and never acted
/// on: files were kept and served forever.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkExportExpiryContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;
    private readonly RowLevelSecurityHarness _rls;

    public BulkExportExpiryContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
        _rls = new RowLevelSecurityHarness(fixture.Postgres);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IBulkJobExecutor Executor => _fixture.Api.Services.GetRequiredService<IBulkJobExecutor>();

    [Fact]
    public async Task An_expired_export_is_not_served_and_its_file_is_removed()
    {
        var table = $"bulkexpiry_{Guid.NewGuid():N}"[..30];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "n", Type = "integer", Nullable = false }],
        }, Ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["n"] = 1 }, Ct)).EnsureSuccessStatusCode();

        var requested = await _client.PostAsJsonAsync($"/api/bulk/{table}/export/csv", new CsvExportApiRequest(), Ct);
        var jobId = JsonDocument.Parse(await requested.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("jobId").GetGuid();
        await As(null, async () =>
        {
            var job = (await Executor.GetPendingExportJobsAsync(int.MaxValue, Ct)).Single(j => j.JobId == jobId);
            await _rls.JobProcessor.ProcessExportJobAsync(Executor, job, Ct);
        });
        (await _client.GetAsync($"/api/bulk/export/{jobId}/download", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "before it expires");
        (await _rls.StoredExportFileAsync(jobId)).Should().NotBeNull();

        await using (var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE morphdb._morph_export_jobs SET expires_at = now() - interval '1 minute' WHERE job_id = @jobId",
                new { jobId });
        }

        var download = await _client.GetAsync($"/api/bulk/export/{jobId}/download", Ct);
        download.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await download.Content.ReadAsStringAsync(Ct)).Should().Contain("EXPORT_EXPIRED");

        (await Executor.DeleteExpiredExportFilesAsync(DateTimeOffset.UtcNow, Ct)).Should().BeGreaterThanOrEqualTo(1);
        (await _rls.StoredExportFileAsync(jobId)).Should().BeNull("the sweep removed the copy");
        (await _client.GetAsync($"/api/bulk/export/{jobId}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the job itself stays readable");
    }
}
