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
/// A name that is taken is answered as a name that is taken -- also when the request that took it is still
/// in flight (a client that retried after a timeout meets its own first attempt), and without a word of
/// PostgreSQL's own, which names physical objects that are not part of the contract.
/// </summary>
[Collection("PostgreSQL")]
public class SchemaDuplicateNameTests
{
    private readonly PostgresFixture _fixture;

    public SchemaDuplicateNameTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Of_two_requests_to_add_the_same_column_the_second_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(Guid.NewGuid(), ct);
        var request = new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "note",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = table.SchemaVersion,
        };

        var (first, second) = await SchemaRace.RunAsync(
            _fixture.Connections, logger => SchemaManager(logger).AddColumnAsync(request, ct), () => SchemaManager().AddColumnAsync(request, ct), ct);

        first.Should().BeNull();
        AssertDuplicate(second, "Column", "note");
    }

    [Fact]
    public async Task Of_two_requests_to_create_the_same_table_the_second_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateTableRequest
        {
            ProjectId = Guid.NewGuid(),
            LogicalName = "dup_" + Suffix(),
            Columns = [new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }],
        };

        var (first, second) = await SchemaRace.RunAsync(
            _fixture.Connections, logger => SchemaManager(logger).CreateTableAsync(request, ct), () => SchemaManager().CreateTableAsync(request, ct), ct);

        first.Should().BeNull();
        AssertDuplicate(second, "Table", request.LogicalName);
    }

    [Fact]
    public async Task Of_two_requests_to_create_the_same_index_the_second_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(Guid.NewGuid(), ct);
        var request = new CreateIndexRequest
        {
            TableId = table.TableId,
            LogicalName = "ix_qty",
            ColumnIds = [table.Columns.Single(c => c.LogicalName == "qty").ColumnId],
            IndexType = IndexType.BTree,
        };

        var (first, second) = await SchemaRace.RunAsync(
            _fixture.Connections, logger => SchemaManager(logger).CreateIndexAsync(request, ct), () => SchemaManager().CreateIndexAsync(request, ct), ct);

        first.Should().BeNull();
        AssertDuplicate(second, "Index", "ix_qty");
    }

    [Fact]
    public async Task A_second_relation_on_the_same_source_column_is_refused_as_a_duplicate_not_a_database_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var parent = await CreateTableAsync(projectId, ct);
        var other = await CreateTableAsync(projectId, ct);
        var child = await CreateTableAsync(projectId, ct, extra: new CreateColumnRequest
        {
            LogicalName = "parent_ref",
            DataType = MorphDataType.Uuid,
            IsNullable = true,
        });
        var source = child.Columns.Single(c => c.LogicalName == "parent_ref");
        CreateRelationRequest Relation(TableMetadata target, string name) => new()
        {
            ProjectId = projectId,
            LogicalName = name,
            SourceTableId = child.TableId,
            SourceColumnId = source.ColumnId,
            TargetTableId = target.TableId,
            TargetColumnId = target.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            RelationType = RelationType.OneToMany,
            EnforceOnWrite = true,
        };

        await SchemaManager().CreateRelationAsync(Relation(parent, "first_relation"), ct);
        var act = () => SchemaManager().CreateRelationAsync(Relation(other, "second_relation"), ct);

        var thrown = (await act.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().BeAssignableTo<SchemaException>("a database error would name a physical constraint");
        thrown.Message.Should().NotContain("fk_");
    }

    [Fact]
    public async Task Of_two_requests_to_create_the_same_view_the_second_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTableAsync(projectId, ct);
        var request = ViewRequest(projectId, table, "dup_view_" + Suffix());

        var (first, second) = await SchemaRace.RunAsync(
            _fixture.Connections, logger => ViewManager(logger).CreateViewAsync(request, ct), () => ViewManager().CreateViewAsync(request, ct), ct);

        first.Should().BeNull();
        AssertDuplicate(second, "View", request.Name);
    }

    [Fact]
    public async Task A_view_renamed_to_the_name_of_another_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTableAsync(projectId, ct);
        var taken = await ViewManager().CreateViewAsync(ViewRequest(projectId, table, "taken_" + Suffix()), ct);
        var other = await ViewManager().CreateViewAsync(ViewRequest(projectId, table, "other_" + Suffix()), ct);

        var act = () => ViewManager().UpdateViewAsync(new UpdateViewRequest { ViewId = other.ViewId, Name = taken.LogicalName }, ct);

        AssertDuplicate((await act.Should().ThrowAsync<Exception>()).Which, "View", taken.LogicalName);
    }

    [Fact]
    public async Task A_table_renamed_to_the_name_a_creation_is_taking_is_a_duplicate_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var existing = await CreateTableAsync(projectId, ct);
        var name = "contested_" + Suffix();
        var creation = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = name,
            Columns = [new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }],
        };

        var (first, second) = await SchemaRace.RunAsync(
            _fixture.Connections,
            logger => SchemaManager(logger).CreateTableAsync(creation, ct),
            () => SchemaManager().UpdateTableAsync(new UpdateTableRequest { TableId = existing.TableId, LogicalName = name, ExpectedVersion = existing.SchemaVersion }, ct),
            ct);

        first.Should().BeNull();
        AssertDuplicate(second, "Table", name);
    }

    private static void AssertDuplicate(Exception? thrown, string entityType, string name)
    {
        var duplicate = thrown.Should().BeOfType<DuplicateNameException>("the name is taken, whoever took it first").Which;
        duplicate.EntityType.Should().Be(entityType);
        duplicate.Name.Should().Be(name);
    }

    private static CreateViewRequest ViewRequest(Guid projectId, TableMetadata table, string name) => new()
    {
        ProjectId = projectId,
        Name = name,
        Definition = new ViewDefinition { BaseTable = table.LogicalName, Columns = [new ViewColumnSpec { Source = "qty", Alias = "qty" }] },
    };

    private PostgresViewManager ViewManager(IChangeLogger? changeLogger = null) => new(
        _fixture.Connections,
        new ViewMetadataRepository(_fixture.Connections),
        new MetadataRepository(_fixture.Connections),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        new Sha256NameHasher(),
        changeLogger ?? new ChangeLogger(_fixture.Connections));

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private async Task<TableMetadata> CreateTableAsync(Guid projectId, CancellationToken ct, CreateColumnRequest? extra = null)
    {
        List<CreateColumnRequest> columns = [new() { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }];
        if (extra is not null)
        {
            columns.Add(extra);
        }

        return await SchemaManager().CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "dupname_" + Suffix(),
            Columns = columns,
        }, ct);
    }

    private PostgresSchemaManager SchemaManager(IChangeLogger? changeLogger = null) => new(
        _fixture.Connections,
        new MetadataRepository(_fixture.Connections),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        new Sha256NameHasher(),
        changeLogger ?? new ChangeLogger(_fixture.Connections),
        new ProjectRepository(_fixture.Connections, new PostgresSchemaNameResolver()),
        new SchemaManagerOptions());
}
