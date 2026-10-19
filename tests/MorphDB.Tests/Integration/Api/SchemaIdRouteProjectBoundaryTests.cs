using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The schema routes that take an id — a column's, an index's, a relation's, the batch's table — reach
/// only what belongs to the caller's project. Another project's id is answered exactly as an id that
/// was never issued is, and the object it names is left as it was.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SchemaIdRouteProjectBoundaryTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Elsewhere(HttpClient Owner, TableApiResponse Parent, TableApiResponse Child)
    {
        public Guid Column => Child.Columns.Single(c => c.Name == "note").Id;
        public Guid Index => Child.Indexes.Single(i => i.Name == "ix_note").Id;
        public Guid Relation => Child.Relations.Single().Id;
    }

    /// <summary>Two linked tables, an index and a spare column, in a project that is not the caller's.</summary>
    private async Task<Elsewhere> ArrangeElsewhereAsync()
    {
        var owner = await fixture.Api.CreateClientWithNewProjectAsync(Ct);
        var parent = $"p_{Guid.NewGuid():N}"[..24];
        var child = $"c_{Guid.NewGuid():N}"[..24];
        (await owner.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = parent,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false, Unique = true }],
        }, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = child,
            Columns =
            [
                new CreateColumnApiRequest { Name = "parent_code", Type = "text", Nullable = true },
                new CreateColumnApiRequest { Name = "note", Type = "text", Nullable = true },
            ],
        }, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/schema/tables/{child}/indexes", new CreateIndexApiRequest
        {
            Name = "ix_note",
            Columns = ["note"],
        }, Ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync("/api/schema/relations", new CreateRelationApiRequest
        {
            Name = "child_parent",
            SourceTable = child,
            SourceColumn = "parent_code",
            TargetTable = parent,
            TargetColumn = "code",
        }, Ct)).EnsureSuccessStatusCode();

        return new Elsewhere(owner, await ReadAsync(owner, parent), await ReadAsync(owner, child));
    }

    private static async Task<TableApiResponse> ReadAsync(HttpClient client, string table)
        => (await client.GetFromJsonAsync<TableApiResponse>($"/api/schema/tables/{table}", Ct))!;

    private static async Task AssertUnchangedAsync(Elsewhere e)
    {
        var child = await ReadAsync(e.Owner, e.Child.Name);
        Assert.Contains(child.Columns, c => c.Id == e.Column && c.Name == "note");
        Assert.Contains(child.Indexes, i => i.Id == e.Index);
        Assert.Contains(child.Relations, r => r.Id == e.Relation);
        Assert.Equal(e.Child.Version, child.Version);
    }

    [Fact]
    public async Task Another_projects_column_cannot_be_altered_or_dropped()
    {
        var e = await ArrangeElsewhereAsync();
        var caller = fixture.Api.Client;

        var rename = new UpdateColumnApiRequest { Name = "renamed", Version = e.Child.Version };
        var byName = $"/api/schema/tables/{e.Child.Name}/columns/note";

        // By name, the table is looked up in the caller's project; by id there is no route at all.
        Assert.Equal(HttpStatusCode.NotFound, (await caller.PatchAsJsonAsync(byName, rename, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caller.DeleteAsync(byName, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caller.PatchAsJsonAsync($"/api/schema/columns/{e.Column}", rename, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caller.DeleteAsync($"/api/schema/columns/{e.Column}", Ct)).StatusCode);
        await AssertUnchangedAsync(e);
    }

    [Fact]
    public async Task Another_projects_index_and_relation_cannot_be_dropped_by_id()
    {
        var e = await ArrangeElsewhereAsync();
        var caller = fixture.Api.Client;

        var index = await caller.DeleteAsync($"/api/schema/indexes/{e.Index}", Ct);
        var relation = await caller.DeleteAsync($"/api/schema/relations/{e.Relation}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, relation.StatusCode);
        await AssertUnchangedAsync(e);
    }

    [Fact]
    public async Task A_schema_batch_cannot_name_another_projects_table()
    {
        var e = await ArrangeElsewhereAsync();

        var response = await fixture.Api.Client.PostAsJsonAsync("/api/schema/batch", new BatchDdlApiRequest
        {
            TableId = e.Child.Id,
            Version = e.Child.Version,
            Operations = [new BatchDdlOperationApiRequest { Type = "deleteColumn", DeleteColumnId = e.Column }],
        }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUnchangedAsync(e);
    }

    [Fact]
    public async Task A_schema_batch_on_the_callers_table_cannot_reach_another_projects_objects()
    {
        var e = await ArrangeElsewhereAsync();
        var caller = fixture.Api.Client;
        var mine = $"m_{Guid.NewGuid():N}"[..24];
        (await caller.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = mine,
            Columns = [new CreateColumnApiRequest { Name = "x", Type = "text", Nullable = true }],
        }, Ct)).EnsureSuccessStatusCode();
        var table = await ReadAsync(caller, mine);

        foreach (var op in new[]
        {
            new BatchDdlOperationApiRequest { Type = "deleteColumn", DeleteColumnId = e.Column },
            new BatchDdlOperationApiRequest { Type = "deleteIndex", DeleteIndexId = e.Index },
            new BatchDdlOperationApiRequest { Type = "deleteRelation", DeleteRelationId = e.Relation },
        })
        {
            var response = await caller.PostAsJsonAsync("/api/schema/batch", new BatchDdlApiRequest
            {
                TableId = table.Id,
                Version = table.Version,
                Operations = [op],
            }, Ct);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{op.Type}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Ct)}");
        }

        await AssertUnchangedAsync(e);
    }
}
