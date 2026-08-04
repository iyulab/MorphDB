using System.Net;
using System.Net.Http.Json;
using Dapper;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A caller that rebuilds its tables wholesale needs every relation non-enforcing, and repeating
/// that declaration on each one is a rule kept by hand — the first relation created without it
/// silently enforces. The project's <c>defaultEnforceOnWrite</c> is where that answer is said once.
/// <para>
/// Three claims are held here, and they are separable: the default reaches a relation that says
/// nothing, a relation that states its own value still wins, and the physical constraint follows
/// the same resolved answer. The third is the one that would rot quietly — a default that reaches
/// the metadata but not the DDL leaves the database rejecting writes the setting says are allowed,
/// which is the switch that only appears to be off.
/// </para>
/// <para>
/// The fourth claim is about time: the default is read when a relation is created, not when it is
/// used. A default consulted at write time would retroactively change relations whose physical
/// constraints were decided under the old answer, so a project flipping it would turn enforcement
/// on for relations that have no constraint to enforce it.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class RelationEnforcementDefaultTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _adminClient;

    public RelationEnforcementDefaultTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Api.Client;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_relation_that_says_nothing_takes_the_project_default(bool projectDefault)
    {
        var (client, _) = await CreateProjectAsync(projectDefault);
        var (parent, child) = await CreateRelatedTablesAsync(client);

        var relation = await CreateRelationAsync(client, parent, child, enforceOnWrite: null);

        relation.EnforceOnWrite.Should().Be(projectDefault,
            "a relation that does not declare enforcement takes the project's standing answer, " +
            "which is the point of having one");
    }

    [Fact]
    public async Task A_relation_that_declares_enforcement_overrides_a_non_enforcing_project()
    {
        var (client, _) = await CreateProjectAsync(defaultEnforceOnWrite: false);
        var (parent, child) = await CreateRelatedTablesAsync(client);

        var relation = await CreateRelationAsync(client, parent, child, enforceOnWrite: true);

        relation.EnforceOnWrite.Should().BeTrue(
            "the project default answers for relations that stay silent; one that speaks is not " +
            "overruled by it");

        var response = await WriteDanglingChildAsync(client, child);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a relation that asked to be checked is checked, whatever the project's default is");
    }

    /// <summary>
    /// Both directions, because one of them is the failure this exists to prevent: a project that
    /// declares non-enforcement and still gets a physical foreign key has an option that is off in
    /// metadata and on in the database, and the write is rejected either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_physical_constraint_follows_the_resolved_default(bool projectDefault)
    {
        var (client, _) = await CreateProjectAsync(projectDefault);
        var (parent, child) = await CreateRelatedTablesAsync(client);
        await CreateRelationAsync(client, parent, child, enforceOnWrite: null);

        (await HasPhysicalForeignKeyAsync(child)).Should().Be(projectDefault,
            "the constraint and the metadata are two halves of one answer; if they can disagree, " +
            "the caller is told one thing and the database does the other");

        var response = await WriteDanglingChildAsync(client, child);
        response.StatusCode.Should().Be(
            projectDefault ? HttpStatusCode.BadRequest : HttpStatusCode.Created,
            "what the write path does is the claim the setting actually makes");
    }

    [Fact]
    public async Task Changing_the_default_leaves_relations_already_created_alone()
    {
        var (client, projectId) = await CreateProjectAsync(defaultEnforceOnWrite: false);
        var (parent, child) = await CreateRelatedTablesAsync(client);
        var relation = await CreateRelationAsync(client, parent, child, enforceOnWrite: null);
        relation.EnforceOnWrite.Should().BeFalse();

        var patched = await _adminClient.PatchAsJsonAsync($"/api/projects/{projectId}", new
        {
            Settings = new ProjectSettingsApiModel { DefaultEnforceOnWrite = true },
        });
        patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());

        // Relations have no read endpoint, so the stored value is read where it lives.
        (await StoredEnforcementAsync(relation.Id)).Should().BeFalse(
            "the default is resolved when a relation is created and stored on it — a later change " +
            "must not rewrite an answer whose physical constraints were decided under the old one");

        var response = await WriteDanglingChildAsync(client, child);
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the relation has no physical constraint behind it, so claiming enforcement after the " +
            "fact would be a claim nothing keeps");
    }

    private async Task<(HttpClient Client, Guid ProjectId)> CreateProjectAsync(bool defaultEnforceOnWrite)
    {
        var response = await _adminClient.PostAsJsonAsync("/api/projects", new
        {
            Name = $"enf_{Guid.NewGuid():N}"[..28],
            Settings = new ProjectSettingsApiModel { DefaultEnforceOnWrite = defaultEnforceOnWrite },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());
        var projectId = (await response.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Id;

        return (_fixture.Api.CreateClientWithProject(projectId), projectId);
    }

    private static async Task<(string Parent, string Child)> CreateRelatedTablesAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var parent = $"defparent_{suffix}";
        var child = $"defchild_{suffix}";

        var parentResponse = await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = parent,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        });
        parentResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await parentResponse.Content.ReadAsStringAsync());

        var childResponse = await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = child,
            Columns =
            [
                new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "parent_ref", Type = "uuid", Nullable = true }
            ]
        });
        childResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await childResponse.Content.ReadAsStringAsync());

        return (parent, child);
    }

    private static async Task<RelationApiResponse> CreateRelationAsync(
        HttpClient client,
        string parent,
        string child,
        bool? enforceOnWrite)
    {
        var response = await client.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = $"fk_{child}_parent",
            SourceTable = child,
            SourceColumn = "parent_ref",
            TargetTable = parent,
            TargetColumn = "_id",
            Type = "one-to-many",
            EnforceOnWrite = enforceOnWrite
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<RelationApiResponse>())!;
    }

    private static Task<HttpResponseMessage> WriteDanglingChildAsync(HttpClient client, string child) =>
        client.PostAsJsonAsync($"/api/data/{child}", new Dictionary<string, object?>
        {
            ["title"] = "child referencing a parent that is not there",
            ["parent_ref"] = Guid.NewGuid().ToString()
        });

    /// <summary>
    /// Logical names are not physical names, so the catalogs are asked about the physical one —
    /// a logical name returns nothing and reads exactly like an absent constraint.
    /// </summary>
    private async Task<bool> HasPhysicalForeignKeyAsync(string logicalName)
    {
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        var physicalName = await connection.QuerySingleAsync<string>(
            "SELECT physical_name FROM morphdb._morph_tables WHERE logical_name = @logicalName AND is_active = true",
            new { logicalName });

        return await connection.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1 FROM pg_constraint con
                JOIN pg_class rel ON rel.oid = con.conrelid
                WHERE rel.relname = @physicalName AND con.contype = 'f')
            """,
            new { physicalName });
    }

    private async Task<bool> StoredEnforcementAsync(Guid relationId)
    {
        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT enforce_on_write FROM morphdb._morph_relations WHERE relation_id = @relationId",
            new { relationId });
    }
}
