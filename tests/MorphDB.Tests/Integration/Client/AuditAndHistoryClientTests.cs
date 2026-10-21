using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's audit log and schema history calls over the real service. Neither had a client method,
/// so a .NET consumer read them by hand — and the audit filter took its category and severity only as
/// numbers while every entry carried them as names, so a filter written from what an entry says was a
/// <c>400</c>.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class AuditAndHistoryClientTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    [Fact]
    public async Task A_write_is_found_in_the_audit_log_by_its_category_read_by_id_and_counted()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = $"aud_{Guid.NewGuid():N}"[..24],
            Columns = [new CreateColumnRequest { Name = "title", Type = "text" }],
        }, Ct);
        var since = DateTimeOffset.UtcNow.AddMinutes(-1);
        await client.Data.InsertAsync(table.Name, new Dictionary<string, object?> { ["title"] = "x" }, Ct);

        // The entry is written as the request finishes, which can be just after the client has its answer.
        AuditLogEntry? entry = null;
        for (var attempt = 0; attempt < 50 && entry is null; attempt++)
        {
            var page = await client.Audit.QueryLogsAsync(fixture.Api.ProjectId, new AuditLogQuery
            {
                Category = AuditCategory.Data,
                From = since,
                PageSize = 100,
            }, Ct);
            page.Items.Should().OnlyContain(e => e.Category == AuditCategory.Data, "the filter by name reaches the service");
            entry = page.Items.FirstOrDefault(e => e.RequestPath == $"/api/data/{table.Name}" && e.HttpMethod == "POST");
            if (entry is null)
                await Task.Delay(100, Ct);
        }

        entry.Should().NotBeNull("the insert is audited");
        entry!.ProjectId.Should().Be(fixture.Api.ProjectId);

        var read = await client.Audit.GetLogAsync(fixture.Api.ProjectId, entry.Id, Ct);
        read!.Id.Should().Be(entry.Id);
        (await client.Audit.GetLogAsync(fixture.Api.ProjectId, Guid.NewGuid(), Ct)).Should().BeNull();

        var stats = await client.Audit.GetStatsAsync(fixture.Api.ProjectId, from: since, cancellationToken: Ct);
        stats.TotalEvents.Should().BeGreaterThan(0);
        stats.ByCategory.Should().ContainKey("data");
    }

    [Fact]
    public async Task A_tables_schema_changes_read_back_in_its_history_and_the_changelog()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = $"his_{Guid.NewGuid():N}"[..24],
            Columns = [new CreateColumnRequest { Name = "title", Type = "text" }],
        }, Ct);
        await client.Schema.AddColumnAsync(table.Name, new AddColumnRequest { Name = "note", Type = "text" }, Ct);

        var history = await client.Schema.GetTableHistoryAsync(table.Name, limit: 10, cancellationToken: Ct);
        history.Should().HaveCountGreaterThanOrEqualTo(2, "the create and the added column");
        history.Should().OnlyContain(c => c.TableId == table.Id);
        history.Select(c => c.SchemaVersion).Should().BeInDescendingOrder("newest first");

        var changelog = await client.Schema.GetChangelogAsync(limit: 500, cancellationToken: Ct);
        changelog.Select(c => c.ChangeId).Should().Contain(history.Select(c => c.ChangeId));

        await Assert.ThrowsAsync<MorphDBNotFoundException>(() => client.Schema.GetTableHistoryAsync("no_such_table_here", cancellationToken: Ct));
    }
}
