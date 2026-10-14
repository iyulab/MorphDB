using System.Net;
using System.Net.Http.Json;
using System.Text;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A bulk job is reached only from the project it belongs to: another project's request answers as
/// an unknown id, on every route that takes a job id.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class BulkJobProjectScopeTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public BulkJobProjectScopeTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private async Task<string> CreateTableWithRowsAsync()
    {
        var tableName = $"bulk_scope_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = tableName,
            Columns = [new CreateColumnApiRequest { Name = "name", Type = "text", Nullable = false }]
        }, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();

        for (var i = 0; i < 3; i++)
        {
            var row = await _client.PostAsJsonAsync($"/api/data/{tableName}",
                new Dictionary<string, object?> { ["name"] = $"row {i}" }, TestContext.Current.CancellationToken);
            row.EnsureSuccessStatusCode();
        }

        return tableName;
    }

    private async Task<Guid> StartExportAsync(string tableName)
    {
        var response = await _client.PostAsJsonAsync($"/api/bulk/{tableName}/export/csv",
            new CsvExportApiRequest { Delimiter = ',', IncludeHeader = true }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<ExportJobApiResponse>(TestContext.Current.CancellationToken);
        return job!.JobId;
    }

    private async Task<Guid> StartImportAsync(string tableName)
    {
        var content = new StringContent("name\nimported\n", Encoding.UTF8, "text/csv");
        var response = await _client.PostAsync($"/api/bulk/{tableName}/import/csv?delimiter=,&hasHeader=true",
            content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var job = await response.Content.ReadFromJsonAsync<ImportJobApiResponse>(TestContext.Current.CancellationToken);
        return job!.JobId;
    }

    [Fact]
    public async Task ExportJob_FromAnotherProject_IsNotFoundOnEveryRoute()
    {
        var jobId = await StartExportAsync(await CreateTableWithRowsAsync());
        using var other = await _fixture.Api.CreateClientWithNewProjectAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        (await other.GetAsync($"/api/bulk/export/{jobId}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/bulk/export/{jobId}/download", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/bulk/jobs/{jobId}/progress", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The owning project still reaches it, and the refused cancel did not cancel it.
        var own = await _client.GetFromJsonAsync<ExportJobApiResponse>($"/api/bulk/export/{jobId}", ct);
        own!.JobId.Should().Be(jobId);
        own.Status.Should().NotBe("cancelled");
        (await _client.GetAsync($"/api/bulk/jobs/{jobId}/progress", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImportJob_FromAnotherProject_IsNotFound()
    {
        var jobId = await StartImportAsync(await CreateTableWithRowsAsync());
        using var other = await _fixture.Api.CreateClientWithNewProjectAsync(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        (await other.GetAsync($"/api/bulk/import/{jobId}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/bulk/jobs/{jobId}/progress", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/bulk/jobs/{jobId}/cancel", null, ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.GetAsync($"/api/bulk/import/{jobId}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
