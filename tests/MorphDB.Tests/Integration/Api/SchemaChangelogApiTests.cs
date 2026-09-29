using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The two changelog routes are documented and had never answered anything but 500: the row type
/// did not match what the driver returns. They also read the log without asking whose it was, and the
/// log records a table's physical name — so the moment they worked they would have served another
/// project's changes, and names that are never part of an answer.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SchemaChangelogApiTests
{
    private readonly ApiIntegrationFixture _fixture;

    public SchemaChangelogApiTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<string> CreateTableAsync(HttpClient client, CancellationToken ct)
    {
        var name = $"clog_{Guid.NewGuid():N}"[..24];
        var response = await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = name,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return name;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route, CancellationToken ct)
    {
        var response = await client.GetAsync(route, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{route}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task A_tables_history_answers_with_its_changes()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(_fixture.Api.Client, ct);

        var history = await GetJsonAsync(_fixture.Api.Client, $"/api/schema/tables/{table}/history", ct);

        var created = history.EnumerateArray().Single(e => e.GetProperty("operation").GetString() == "CreateTable");
        created.GetProperty("changes").GetProperty("logicalName").GetString().Should().Be(table);
    }

    [Fact]
    public async Task The_changelog_lists_this_projects_changes_and_no_other_projects()
    {
        var ct = TestContext.Current.CancellationToken;
        var own = await CreateTableAsync(_fixture.Api.Client, ct);
        using var other = await _fixture.Api.CreateClientWithNewProjectAsync(ct);
        var theirs = await CreateTableAsync(other, ct);

        var changelog = await GetJsonAsync(_fixture.Api.Client, "/api/schema/changelog?limit=500", ct);

        var names = changelog.EnumerateArray()
            .Select(e => e.GetProperty("changes").TryGetProperty("logicalName", out var n) ? n.GetString() : null)
            .ToList();
        names.Should().Contain(own);
        names.Should().NotContain(theirs, "another project's schema changes are not this project's to read");
    }

    [Fact]
    public async Task No_change_answers_with_a_physical_name_even_one_recorded_before_this_was_fixed()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(_fixture.Api.Client, ct);
        await using (var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString))
        {
            // What earlier releases wrote for a created table.
            await connection.ExecuteAsync("""
                INSERT INTO morphdb._morph_changelog (table_id, operation, schema_version, changes)
                SELECT table_id, 'CreateTable', 1,
                       jsonb_build_object('logicalName', logical_name, 'physicalName', physical_name, 'columnCount', 1)
                FROM morphdb._morph_tables
                WHERE logical_name = @Table AND project_id = @Project AND is_active
                """, new { Table = table, Project = _fixture.Api.ProjectId });
        }

        foreach (var route in new[] { $"/api/schema/tables/{table}/history", "/api/schema/changelog?limit=500" })
        {
            var body = (await GetJsonAsync(_fixture.Api.Client, route, ct)).GetRawText();
            body.Should().NotContain("physicalName", route).And.NotContain("tbl_", route);
        }
    }
}
