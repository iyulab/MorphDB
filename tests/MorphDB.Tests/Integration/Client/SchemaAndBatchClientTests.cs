using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's schema and batch calls over the real service: every method reaches a route the
/// service has, and what it reads back is what the service did. Altering and dropping a column used
/// to name a route the service never had, so neither could succeed.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class SchemaAndBatchClientTests(ApiIntegrationFixture fixture)
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

    [Fact]
    public async Task A_column_is_altered_and_dropped_by_its_table_and_name()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("alt"),
            Columns =
            [
                new CreateColumnRequest { Name = "title", Type = "text" },
                new CreateColumnRequest { Name = "note", Type = "text" },
            ],
        }, Ct);

        var renamed = await client.Schema.AlterColumnAsync(table.Name, "note", new AlterColumnRequest
        {
            Name = "remark",
            Nullable = false,
            Default = "'-'",
            Version = table.Version,
        }, Ct);
        Assert.Equal("remark", renamed.Name);
        Assert.False(renamed.Nullable);

        var afterRename = await client.Schema.GetTableAsync(table.Name, Ct);
        Assert.NotNull(afterRename);
        Assert.Contains(afterRename.Columns, c => c.Name == "remark");
        Assert.DoesNotContain(afterRename.Columns, c => c.Name == "note");

        await client.Schema.DropColumnAsync(table.Name, "remark", Ct);
        var afterDrop = await client.Schema.GetTableAsync(table.Name, Ct);
        Assert.DoesNotContain(afterDrop!.Columns, c => c.Name == "remark");

        // A column that is not there is the service's 404, which the client raises as such.
        await Assert.ThrowsAsync<MorphDBNotFoundException>(() => client.Schema.DropColumnAsync(table.Name, "remark", Ct));
    }

    [Fact]
    public async Task Tables_list_and_a_relation_is_deleted_by_its_id()
    {
        await using var client = NewClient();
        var parent = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("par"),
            Columns = [new CreateColumnRequest { Name = "code", Type = "text", Nullable = false, Unique = true }],
        }, Ct);
        var child = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("chi"),
            Columns = [new CreateColumnRequest { Name = "parent_code", Type = "text" }],
        }, Ct);

        var tables = await client.Schema.GetTablesAsync(Ct);
        Assert.Contains(tables, t => t.Name == parent.Name && t.Columns.Any(c => c.Name == "code"));
        Assert.Contains(tables, t => t.Name == child.Name);

        var relation = await client.Schema.CreateRelationAsync(new CreateRelationRequest
        {
            Name = Name("rel"),
            SourceTable = child.Name,
            SourceColumn = "parent_code",
            TargetTable = parent.Name,
            TargetColumn = "code",
        }, Ct);

        // Each side reports the relation with its ends, so a caller can find what refers to a table.
        foreach (var side in new[] { parent, child })
        {
            var reported = Assert.Single((await client.Schema.GetTableAsync(side.Name, Ct))!.Relations);
            Assert.Equal(relation.Id, reported.Id);
            Assert.Equal(child.Id, reported.SourceTableId);
            Assert.Equal(parent.Id, reported.TargetTableId);
            Assert.Equal(child.Columns.Single(c => c.Name == "parent_code").Id, reported.SourceColumnId);
        }

        await client.Schema.DeleteRelationAsync(relation.Id, Ct);
        Assert.Empty((await client.Schema.GetTableAsync(parent.Name, Ct))!.Relations);

        // The relation is gone: a child row naming no parent is written, where the relation refused it.
        await client.Data.InsertAsync(child.Name, new Dictionary<string, object?> { ["parent_code"] = "nobody" }, Ct);
        await Assert.ThrowsAsync<MorphDBNotFoundException>(() => client.Schema.DeleteRelationAsync(relation.Id, Ct));
    }

    [Fact]
    public async Task An_index_is_created_over_named_columns_reported_with_its_table_and_deleted()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("idx"),
            Columns =
            [
                new CreateColumnRequest { Name = "kind", Type = "text" },
                new CreateColumnRequest { Name = "at", Type = "bigint" },
            ],
        }, Ct);

        var index = await client.Schema.CreateIndexAsync(table.Name, new CreateIndexRequest { Name = "by_kind_at", Columns = ["kind", "at"] }, Ct);

        Assert.Equal(["kind", "at"], index.Columns);
        Assert.Equal("btree", index.Type);
        var reported = Assert.Single((await client.Schema.GetTableAsync(table.Name, Ct))!.Indexes, i => i.Name == "by_kind_at");
        Assert.Equal(index.Id, reported.Id);
        Assert.Equal(["kind", "at"], reported.Columns);

        await Assert.ThrowsAnyAsync<MorphDBApiException>(() =>
            client.Schema.CreateIndexAsync(table.Name, new CreateIndexRequest { Name = "by_missing", Columns = ["missing"] }, Ct));

        await client.Schema.DeleteIndexAsync(index.Id, Ct);
        Assert.DoesNotContain((await client.Schema.GetTableAsync(table.Name, Ct))!.Indexes, i => i.Name == "by_kind_at");
    }

    [Fact]
    public async Task Rows_are_updated_and_deleted_by_filter_and_upserted_and_seeded_by_key()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("bulk"),
            Columns =
            [
                new CreateColumnRequest { Name = "sku", Type = "text", Nullable = false, Unique = true },
                new CreateColumnRequest { Name = "qty", Type = "integer" },
            ],
        }, Ct);
        await client.Batch.InsertManyAsync(table.Name,
        [
            new Dictionary<string, object?> { ["sku"] = "a", ["qty"] = 1 },
            new Dictionary<string, object?> { ["sku"] = "b", ["qty"] = 2 },
            new Dictionary<string, object?> { ["sku"] = "c", ["qty"] = 3 },
        ], Ct);

        var updated = await client.Batch.UpdateManyAsync(table.Name, [new Filter("qty", FilterOperator.GreaterThanOrEqual, 2)], new Dictionary<string, object?> { ["qty"] = 10 }, Ct);
        Assert.Equal(2, updated.Results.Single().AffectedRows);

        var deleted = await client.Batch.DeleteManyAsync(table.Name, [new Filter("sku", FilterOperator.Equal, "a")], Ct);
        Assert.Equal(1, deleted.Results.Single().AffectedRows);

        await client.Data.UpsertAsync(table.Name, new Dictionary<string, object?> { ["sku"] = "b", ["qty"] = 20 }, ["sku"], Ct);
        await client.Data.UpsertAsync(table.Name, new Dictionary<string, object?> { ["sku"] = "d", ["qty"] = 4 }, ["sku"], Ct);

        var seed = new List<IDictionary<string, object?>> { new Dictionary<string, object?> { ["sku"] = "e", ["qty"] = 5 } };
        var first = await client.Batch.SeedAsync(table.Name, seed, ["sku"], Ct);
        seed[0]["qty"] = 50;
        var again = await client.Batch.SeedAsync(table.Name, seed, ["sku"], Ct);
        Assert.Equal((1, 0), (first.Inserted, first.Updated));
        Assert.Equal((0, 1), (again.Inserted, again.Updated));

        var rows = (await client.Data.QueryAsync(table.Name, new QueryRequest(), Ct)).Data
            .ToDictionary(r => r.Data["sku"]!.ToString()!, r => Convert.ToInt64(r.Data["qty"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new Dictionary<string, long> { ["b"] = 20, ["c"] = 10, ["d"] = 4, ["e"] = 50 }, rows);
    }

    private static readonly string[] LotsOneAndTwo = ["L-1", "L-2"];

    [Fact]
    public async Task A_filter_tree_selects_the_rows_any_of_its_groups_admit()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("tree"),
            Columns =
            [
                new CreateColumnRequest { Name = "sku", Type = "text" },
                new CreateColumnRequest { Name = "qty", Type = "integer" },
                new CreateColumnRequest { Name = "lot", Type = "text" },
            ],
        }, Ct);
        await client.Batch.InsertManyAsync(table.Name,
        [
            new Dictionary<string, object?> { ["sku"] = "a", ["qty"] = 1, ["lot"] = "L-1" },
            new Dictionary<string, object?> { ["sku"] = "b", ["qty"] = 5, ["lot"] = "L-9" },
            new Dictionary<string, object?> { ["sku"] = "c", ["qty"] = 2, ["lot"] = "L-9" },
            new Dictionary<string, object?> { ["sku"] = "d", ["qty"] = 7, ["lot"] = "L-2" },
        ], Ct);

        var page = await client.Data.QueryAsync(table.Name, new QueryRequest
        {
            Filters = [new Filter("qty", FilterOperator.LessThan, 7)],
            Where = FilterGroup.Or(new Filter("qty", FilterOperator.GreaterThanOrEqual, 5), new Filter("lot", FilterOperator.In, LotsOneAndTwo)),
            OrderBy = [new OrderBy("sku")],
        }, Ct);

        Assert.Equal(["a", "b"], page.Data.Select(r => r.Data["sku"]!.ToString()));
    }

    [Fact]
    public async Task A_batch_runs_its_operations_in_order_and_reports_each()
    {
        await using var client = NewClient();
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = Name("bat"),
            Columns =
            [
                new CreateColumnRequest { Name = "sku", Type = "text", Nullable = false, Unique = true },
                new CreateColumnRequest { Name = "qty", Type = "integer" },
            ],
        }, Ct);

        var many = await client.Batch.InsertManyAsync(table.Name,
        [
            new Dictionary<string, object?> { ["sku"] = "a", ["qty"] = 1 },
            new Dictionary<string, object?> { ["sku"] = "b", ["qty"] = 2 },
        ], Ct);
        Assert.Equal(2, many.SuccessCount);
        Assert.Equal(0, many.FailureCount);
        var a = (await client.Data.QueryAsync(table.Name, new QueryRequest
        {
            Filters = [new Filter("sku", FilterOperator.Equal, "a")],
        }, Ct)).Data.Single();
        var aId = a.Id;

        var batch = await client.Batch.ExecuteAsync(new BatchRequest
        {
            Operations =
            [
                new BatchOperation { Method = BatchMethod.Update, Table = table.Name, Id = aId, Data = new Dictionary<string, object?> { ["qty"] = 10 } },
                new BatchOperation { Method = BatchMethod.Upsert, Table = table.Name, KeyColumns = ["sku"], Data = new Dictionary<string, object?> { ["sku"] = "c", ["qty"] = 3 } },
                new BatchOperation { Method = BatchMethod.Insert, Table = table.Name, Data = new Dictionary<string, object?> { ["sku"] = "c", ["qty"] = 4 } },
            ],
        }, Ct);

        Assert.Equal([0, 1, 2], batch.Results.Select(r => r.Index));
        Assert.True(batch.Results[0].Success);
        Assert.True(batch.Results[1].Success);
        Assert.False(batch.Results[2].Success); // "c" is taken by the upsert before it
        Assert.False(string.IsNullOrEmpty(batch.Results[2].Error));
        Assert.Equal(2, batch.SuccessCount);
        Assert.Equal(1, batch.FailureCount);

        var rows = (await client.Data.QueryAsync(table.Name, new QueryRequest(), Ct)).Data
            .ToDictionary(r => r.Data["sku"]!.ToString()!, r => int.Parse(r.Data["qty"]!.ToString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(new Dictionary<string, int> { ["a"] = 10, ["b"] = 2, ["c"] = 3 }, rows);
    }
}
