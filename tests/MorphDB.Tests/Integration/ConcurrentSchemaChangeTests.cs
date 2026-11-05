using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A schema change is one transaction that holds the table's physical lock until its description is
/// written. Changes made at the same moment -- to one table, to two, and across a relation between them in
/// both directions -- must all finish, none of them stalling on another, and leave every physical column
/// and index with a description and every description with its physical object.
/// </summary>
[Collection("PostgreSQL")]
public class ConcurrentSchemaChangeTests
{
    private readonly PostgresFixture _fixture;

    public ConcurrentSchemaChangeTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Schema_changes_made_at_the_same_time_all_finish_and_leave_objects_and_descriptions_in_agreement()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var schema = SchemaManager();
        var repository = new MetadataRepository(_fixture.Connections);

        var a = await CreateTableAsync(schema, projectId, "conc_a_", ct);
        var b = await CreateTableAsync(schema, projectId, "conc_b_", ct);
        var aScratch = a.Columns.Single(c => c.LogicalName == "scratch");
        var bScratch = b.Columns.Single(c => c.LogicalName == "scratch");

        var work = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            var n = i;
            work.Add(Task.Run(() => AddColumnAsync(schema, repository, a.TableId, $"a_added_{n}", ct), ct));
            work.Add(Task.Run(() => AddColumnAsync(schema, repository, b.TableId, $"b_added_{n}", ct), ct));
        }

        // Relations in both directions: each takes both tables' locks.
        work.Add(Task.Run(() => schema.CreateRelationAsync(Relation(projectId, a, "parent_ref", b, "rel_a_to_b"), ct), ct));
        work.Add(Task.Run(() => schema.CreateRelationAsync(Relation(projectId, b, "parent_ref", a, "rel_b_to_a"), ct), ct));
        work.Add(Task.Run(() => schema.CreateIndexAsync(IndexOn(a, "qty", "ix_a_qty"), ct), ct));
        work.Add(Task.Run(() => schema.CreateIndexAsync(IndexOn(b, "qty", "ix_b_qty"), ct), ct));
        work.Add(Task.Run(() => schema.DeleteColumnAsync(aScratch.ColumnId, ct), ct));
        work.Add(Task.Run(() => schema.DeleteColumnAsync(bScratch.ColumnId, ct), ct));

        var all = Task.WhenAll(work);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(90), ct));
        finished.Should().BeSameAs(all, "no schema change may stall on another");
        await all;

        foreach (var table in new[] { a, b })
        {
            var described = await repository.GetTableByIdAsync(table.TableId, includeColumns: true, ct);
            var physical = await PhysicalColumnsAsync(table.PhysicalName, ct);

            described!.Columns.Select(c => c.PhysicalName).Should().BeEquivalentTo(physical, "every column has a description and every description a column");
            described.Columns.Should().NotContain(c => c.LogicalName == "scratch");
            described.Columns.Count(c => c.LogicalName.Contains("_added_")).Should().Be(5);
            described.Indexes.Should().ContainSingle();
            (await PhysicalIndexesAsync(described.Indexes.Single().PhysicalName, ct)).Should().Be(1);
            described.Relations.Should().HaveCount(2, "both relations touch both tables");
        }
    }

    [Fact]
    public async Task Many_tables_created_at_the_same_moment_all_exist_with_their_physical_tables()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var schema = SchemaManager();
        var repository = new MetadataRepository(_fixture.Connections);
        var names = Enumerable.Range(0, 24).Select(i => $"burst_{i}_{Guid.NewGuid().ToString("N")[..6]}").ToList();

        var creations = names.Select((name, i) => Task.Run(() => schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = name,
            Columns =
            [
                new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true, IsIndexed = i % 2 == 0 },
                new CreateColumnRequest { LogicalName = "label", DataType = MorphDataType.Text, IsNullable = true, IsUnique = i % 3 == 0 },
            ],
        }, ct), ct)).ToList();

        var all = Task.WhenAll(creations);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(120), ct));
        finished.Should().BeSameAs(all, "no creation may stall on another");
        var created = await all;

        created.Select(t => t.LogicalName).Should().BeEquivalentTo(names);
        var listed = await repository.ListTablesAsync(projectId, includeColumns: true, ct);
        listed.Should().HaveCount(names.Count);
        foreach (var table in listed)
        {
            (await PhysicalColumnsAsync(table.PhysicalName, ct)).Should().BeEquivalentTo(table.Columns.Select(c => c.PhysicalName));
        }
    }

    private static async Task AddColumnAsync(PostgresSchemaManager schema, MetadataRepository repository, Guid tableId, string name, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var version = await repository.GetCurrentVersionAsync(tableId, cancellationToken: ct);
            try
            {
                await schema.AddColumnAsync(new AddColumnRequest
                {
                    TableId = tableId,
                    LogicalName = name,
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    ExpectedVersion = version,
                }, ct);
                return;
            }
            catch (SchemaVersionConflictException)
            {
                await Task.Delay(Random.Shared.Next(5, 25), ct);
            }
        }

        throw new TimeoutException($"{name} was never added: every attempt met a newer version.");
    }

    private static CreateRelationRequest Relation(Guid projectId, TableMetadata from, string column, TableMetadata to, string name) => new()
    {
        ProjectId = projectId,
        LogicalName = name,
        SourceTableId = from.TableId,
        SourceColumnId = from.Columns.Single(c => c.LogicalName == column).ColumnId,
        TargetTableId = to.TableId,
        TargetColumnId = to.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
        RelationType = RelationType.OneToMany,
        EnforceOnWrite = true,
    };

    private static CreateIndexRequest IndexOn(TableMetadata table, string column, string name) => new()
    {
        TableId = table.TableId,
        LogicalName = name,
        ColumnIds = [table.Columns.Single(c => c.LogicalName == column).ColumnId],
        IndexType = IndexType.BTree,
    };

    private static async Task<TableMetadata> CreateTableAsync(PostgresSchemaManager schema, Guid projectId, string prefix, CancellationToken ct) =>
        await schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = prefix + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true },
                new CreateColumnRequest { LogicalName = "scratch", DataType = MorphDataType.Text, IsNullable = true },
                new CreateColumnRequest { LogicalName = "parent_ref", DataType = MorphDataType.Uuid, IsNullable = true },
            ],
        }, ct);

    private async Task<IReadOnlyList<string>> PhysicalColumnsAsync(string physicalTable, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return (await connection.QueryAsync<string>(
            "SELECT a.attname FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid " +
            "WHERE c.relname = @Table AND a.attnum > 0 AND NOT a.attisdropped",
            new { Table = physicalTable })).ToList();
    }

    private async Task<long> PhysicalIndexesAsync(string physicalIndex, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_class WHERE relkind = 'i' AND relname = @Name", new { Name = physicalIndex });
    }

    private PostgresSchemaManager SchemaManager() => new(
        _fixture.Connections,
        new MetadataRepository(_fixture.Connections),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        new Sha256NameHasher(),
        new ChangeLogger(_fixture.Connections),
        new ProjectRepository(_fixture.Connections, new PostgresSchemaNameResolver()),
        new SchemaManagerOptions());
}
