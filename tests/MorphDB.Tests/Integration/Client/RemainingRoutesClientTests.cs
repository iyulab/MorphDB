using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's table and view changes, a materialized view's staleness, and the bulk job lists and
/// progress, over the real service. Each route was served and documented with no client method.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class RemainingRoutesClientTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    private static string Name(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];

    private static async Task<TableInfo> TableAsync(MorphDBClient client)
    {
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("rem"),
            Columns = [new CreateColumnRequest { Name = "title", Type = "text" }],
        }, Ct);
        await client.Data.InsertAsync(table.Name, new Dictionary<string, object?> { ["title"] = "first" }, Ct);
        return table;
    }

    [Fact]
    public async Task A_table_is_renamed_against_the_version_last_read()
    {
        await using var client = NewClient();
        var table = await TableAsync(client);
        var renamed = Name("ren");

        var updated = await client.Schema.UpdateTableAsync(table.Name, new UpdateTableRequest { Name = renamed, Version = table.Version }, Ct);
        updated.Name.Should().Be(renamed);
        updated.Version.Should().BeGreaterThan(table.Version);
        (await client.Schema.GetTableAsync(renamed, Ct)).Should().NotBeNull();

        // The version read before the rename is now behind.
        await Assert.ThrowsAsync<MorphDBConflictException>(() =>
            client.Schema.UpdateTableAsync(renamed, new UpdateTableRequest { Name = Name("again"), Version = table.Version }, Ct));
    }

    [Fact]
    public async Task A_view_is_changed_and_a_materialized_one_says_whether_it_is_stale()
    {
        await using var client = NewClient();
        var table = await TableAsync(client);

        var plain = await client.Views.CreateAsync(new CreateViewRequest
        {
            Name = Name("v"),
            BaseTable = table.Name,
            Columns = [new ViewColumn { Source = "title", Alias = "title" }],
        }, Ct);
        var changed = await client.Views.UpdateAsync(plain.Name, new UpdateViewRequest { Description = "changed", Limit = 5 }, Ct);
        changed.Limit.Should().Be(5);
        changed.Columns.Should().ContainSingle(c => c.Name == "title", "members left null keep the definition");
        await Assert.ThrowsAsync<MorphDBValidationException>(() => client.Views.GetStalenessAsync(plain.Name, Ct));

        var materialized = await client.Views.CreateAsync(new CreateViewRequest
        {
            Name = Name("mv"),
            BaseTable = table.Name,
            Columns = [new ViewColumn { Source = "title", Alias = "title" }],
            Materialized = true,
        }, Ct);
        var staleness = await client.Views.GetStalenessAsync(materialized.Name, Ct);
        staleness.Should().NotBeNull();

        await client.Views.DeleteAsync(materialized.Name, Ct);
        await client.Views.DeleteAsync(plain.Name, Ct);
    }

    [Fact]
    public async Task Bulk_jobs_are_listed_and_a_jobs_progress_is_read()
    {
        await using var client = NewClient();
        var table = await TableAsync(client);

        var export = await client.Bulk.ExportJsonAsync(table.Name, cancellationToken: Ct);

        var exports = await client.Bulk.ListExportJobsAsync(limit: 200, cancellationToken: Ct);
        exports.Should().Contain(j => j.JobId == export.JobId);
        (await client.Bulk.ListImportJobsAsync(limit: 1, cancellationToken: Ct)).Count.Should().BeLessThanOrEqualTo(1);

        var progress = await client.Bulk.GetJobProgressAsync(export.JobId, Ct);
        progress.Should().NotBeNull();
        progress!.JobId.Should().Be(export.JobId);
        progress.Status.Should().NotBeNullOrEmpty();

        (await client.Bulk.GetJobProgressAsync(Guid.NewGuid(), Ct)).Should().BeNull();
    }
}
