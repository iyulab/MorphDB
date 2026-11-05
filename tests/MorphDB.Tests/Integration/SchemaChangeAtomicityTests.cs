using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A schema change is a physical change and a description of it. When the description cannot be written,
/// the caller sees that failure -- and the change must not have happened: no column, index or constraint
/// that nothing describes, and the same request is answered the same way when it is sent again.
/// </summary>
[Collection("PostgreSQL")]
public class SchemaChangeAtomicityTests
{
    private readonly PostgresFixture _fixture;
    private readonly Sha256NameHasher _hasher = new();

    public SchemaChangeAtomicityTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_column_whose_change_cannot_be_logged_is_not_added_and_can_be_added_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var request = new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "note",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = table.SchemaVersion,
        };

        var act = () => SchemaManager(new ThrowingChangeLogger()).AddColumnAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await PhysicalColumnsAsync(table.PhysicalName, _hasher.GenerateColumnName(table.TableId, "note"), ct))
            .Should().Be(0, "the column a failed change added is taken back with it");
        var after = await Repository().GetTableByIdAsync(table.TableId, includeColumns: true, ct);
        after!.Columns.Should().NotContain(c => c.LogicalName == "note");
        after.SchemaVersion.Should().Be(table.SchemaVersion, "a change that did not happen does not move the version");

        var added = await SchemaManager().AddColumnAsync(request, ct);
        added.LogicalName.Should().Be("note", "the same request, sent again, is answered as if the first never happened");
    }

    [Fact]
    public async Task An_index_whose_change_cannot_be_logged_is_not_built_and_can_be_built_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var request = new CreateIndexRequest
        {
            TableId = table.TableId,
            LogicalName = "ix_qty",
            ColumnIds = [table.Columns.Single(c => c.LogicalName == "qty").ColumnId],
            IndexType = IndexType.BTree,
        };

        var act = () => SchemaManager(new ThrowingChangeLogger()).CreateIndexAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await RelationsNamedAsync(_hasher.GenerateIndexName(table.TableId, "ix_qty"), "i", ct))
            .Should().Be(0, "the index a failed change built is taken back with it");
        (await Repository().GetIndexesByTableIdAsync(table.TableId, ct)).Should().BeEmpty();

        var built = await SchemaManager().CreateIndexAsync(request, ct);
        built.LogicalName.Should().Be("ix_qty");
    }

    [Fact]
    public async Task A_relation_whose_change_cannot_be_logged_leaves_no_constraint_and_can_be_created_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var parent = await CreateTableAsync(SchemaManager(), projectId, ct);
        var child = await CreateTableAsync(SchemaManager(), projectId, ct, extra: new CreateColumnRequest
        {
            LogicalName = "parent_ref",
            DataType = MorphDataType.Uuid,
            IsNullable = true,
        });
        var source = child.Columns.Single(c => c.LogicalName == "parent_ref");
        var request = new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "child_parent",
            SourceTableId = child.TableId,
            SourceColumnId = source.ColumnId,
            TargetTableId = parent.TableId,
            TargetColumnId = parent.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            RelationType = RelationType.OneToMany,
            EnforceOnWrite = true,
        };

        var act = () => SchemaManager(new ThrowingChangeLogger()).CreateRelationAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        await using (var connection = await _fixture.DataSource.OpenConnectionAsync(ct))
        {
            var constraints = await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_constraint WHERE conname = @Name",
                new { Name = $"fk_{child.PhysicalName}_{source.PhysicalName}" });
            constraints.Should().Be(0, "the foreign key a failed change added is taken back with it");
        }

        var created = await SchemaManager().CreateRelationAsync(request, ct);
        created.LogicalName.Should().Be("child_parent");
    }

    [Fact]
    public async Task A_many_to_many_relation_whose_change_cannot_be_logged_leaves_no_junction_table()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var left = await CreateTableAsync(SchemaManager(), projectId, ct);
        var right = await CreateTableAsync(SchemaManager(), projectId, ct);
        var junction = "junction_" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "left_right",
            SourceTableId = left.TableId,
            SourceColumnId = left.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            TargetTableId = right.TableId,
            TargetColumnId = right.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            RelationType = RelationType.ManyToMany,
            JunctionTableName = junction,
        };

        // The junction table is a table of its own, created and logged as one; only the relation's own
        // change is refused, so the failure arrives after the junction table is complete.
        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.CreateRelation)).CreateRelationAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await RelationsNamedAsync(_hasher.GenerateTableName(projectId, junction), "r", ct))
            .Should().Be(0, "the junction table a failed change built is taken back with it");
        (await SchemaManager().GetTableAsync(projectId, junction, ct)).Should().BeNull();

        var created = await SchemaManager().CreateRelationAsync(request, ct);
        created.LogicalName.Should().Be("left_right");
        (await SchemaManager().GetTableAsync(projectId, junction, ct)).Should().NotBeNull("the junction name is free again");
    }

    [Fact]
    public async Task A_column_whose_deletion_cannot_be_logged_is_still_there_and_can_be_deleted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var note = (await SchemaManager().AddColumnAsync(new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "note",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = table.SchemaVersion,
        }, ct));

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.DeleteColumn)).DeleteColumnAsync(note.ColumnId, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await PhysicalColumnsAsync(table.PhysicalName, note.PhysicalName, ct))
            .Should().Be(1, "a deletion that did not happen leaves the column where it was");
        (await Repository().GetTableByIdAsync(table.TableId, includeColumns: true, ct))!
            .Columns.Should().Contain(c => c.LogicalName == "note", "and its description with it");

        await SchemaManager().DeleteColumnAsync(note.ColumnId, ct);
        (await PhysicalColumnsAsync(table.PhysicalName, note.PhysicalName, ct)).Should().Be(0);
    }

    [Fact]
    public async Task An_index_whose_deletion_cannot_be_logged_is_still_there_and_can_be_deleted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var index = await SchemaManager().CreateIndexAsync(new CreateIndexRequest
        {
            TableId = table.TableId,
            LogicalName = "ix_qty",
            ColumnIds = [table.Columns.Single(c => c.LogicalName == "qty").ColumnId],
            IndexType = IndexType.BTree,
        }, ct);

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.DeleteIndex)).DeleteIndexAsync(index.IndexId, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await RelationsNamedAsync(index.PhysicalName, "i", ct)).Should().Be(1, "a deletion that did not happen leaves the index where it was");
        (await Repository().GetIndexesByTableIdAsync(table.TableId, ct)).Should().ContainSingle("and its description with it");

        await SchemaManager().DeleteIndexAsync(index.IndexId, ct);
        (await RelationsNamedAsync(index.PhysicalName, "i", ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_relation_whose_deletion_cannot_be_logged_keeps_its_constraint_and_can_be_deleted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var parent = await CreateTableAsync(SchemaManager(), projectId, ct);
        var child = await CreateTableAsync(SchemaManager(), projectId, ct, extra: new CreateColumnRequest
        {
            LogicalName = "parent_ref",
            DataType = MorphDataType.Uuid,
            IsNullable = true,
        });
        var source = child.Columns.Single(c => c.LogicalName == "parent_ref");
        var relation = await SchemaManager().CreateRelationAsync(new CreateRelationRequest
        {
            ProjectId = projectId,
            LogicalName = "child_parent",
            SourceTableId = child.TableId,
            SourceColumnId = source.ColumnId,
            TargetTableId = parent.TableId,
            TargetColumnId = parent.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            RelationType = RelationType.OneToMany,
            EnforceOnWrite = true,
        }, ct);

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.DeleteRelation)).DeleteRelationAsync(relation.RelationId, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        var constraint = $"fk_{child.PhysicalName}_{source.PhysicalName}";
        (await ConstraintsNamedAsync(constraint, ct)).Should().Be(1, "a deletion that did not happen leaves the foreign key where it was");
        (await Repository().GetRelationByIdAsync(relation.RelationId, ct)).Should().NotBeNull("and its description with it");

        await SchemaManager().DeleteRelationAsync(relation.RelationId, ct);
        (await ConstraintsNamedAsync(constraint, ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_table_whose_deletion_cannot_be_logged_is_still_there_and_can_be_deleted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.DeleteTable)).DeleteTableAsync(table.TableId, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await RelationsNamedAsync(table.PhysicalName, "r", ct)).Should().Be(1, "a deletion that did not happen leaves the table where it was");
        (await Repository().GetTableByIdAsync(table.TableId, includeColumns: true, ct)).Should().NotBeNull("and its description with it");

        await SchemaManager().DeleteTableAsync(table.TableId, ct);
        (await RelationsNamedAsync(table.PhysicalName, "r", ct)).Should().Be(0);
    }

    [Fact]
    public async Task A_column_whose_type_change_cannot_be_logged_keeps_its_type_and_can_be_changed_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var qty = table.Columns.Single(c => c.LogicalName == "qty");
        var typeBefore = await PhysicalTypeAsync(table.PhysicalName, qty.PhysicalName, ct);
        var version = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct);
        var request = new UpdateColumnRequest
        {
            ColumnId = qty.ColumnId,
            DataType = MorphDataType.Decimal,
            ForceCast = true,
            ExpectedVersion = version,
        };

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.UpdateColumn)).UpdateColumnAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await PhysicalTypeAsync(table.PhysicalName, qty.PhysicalName, ct)).Should().Be(typeBefore, "a change that did not happen leaves the column's type alone");
        (await Repository().GetColumnByIdAsync(qty.ColumnId, ct))!.DataType.Should().Be(MorphDataType.Integer, "and so does its description");
        (await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct)).Should().Be(version);

        var changed = await SchemaManager().UpdateColumnAsync(request, ct);
        changed.DataType.Should().Be(MorphDataType.Decimal);
        (await PhysicalTypeAsync(table.PhysicalName, qty.PhysicalName, ct)).Should().NotBe(typeBefore);
    }

    [Fact]
    public async Task A_column_whose_new_logical_name_cannot_be_logged_keeps_its_name_and_can_be_renamed_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var qty = table.Columns.Single(c => c.LogicalName == "qty");
        var request = new UpdateColumnRequest
        {
            ColumnId = qty.ColumnId,
            LogicalName = "amount",
            ExpectedVersion = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct),
        };

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.UpdateColumn)).UpdateColumnAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await Repository().GetColumnByIdAsync(qty.ColumnId, ct))!.LogicalName.Should().Be("qty", "a rename that did not happen is not in the description");

        var renamed = await SchemaManager().UpdateColumnAsync(request, ct);
        renamed.LogicalName.Should().Be("amount");
    }

    [Fact]
    public async Task A_column_whose_rename_cannot_be_logged_keeps_its_physical_name_and_can_be_renamed_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var qty = table.Columns.Single(c => c.LogicalName == "qty");
        var renamedPhysical = _hasher.GenerateColumnName(table.TableId, "amount");
        var request = new RenameColumnRequest
        {
            ColumnId = qty.ColumnId,
            NewLogicalName = "amount",
            ExpectedVersion = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct),
        };

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.RenameColumn)).RenameColumnAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await PhysicalColumnsAsync(table.PhysicalName, qty.PhysicalName, ct)).Should().Be(1, "a rename that did not happen leaves the column under its name");
        (await PhysicalColumnsAsync(table.PhysicalName, renamedPhysical, ct)).Should().Be(0);
        (await Repository().GetColumnByIdAsync(qty.ColumnId, ct))!.LogicalName.Should().Be("qty");

        var renamed = await SchemaManager().RenameColumnAsync(request, ct);
        renamed.LogicalName.Should().Be("amount");
        (await PhysicalColumnsAsync(table.PhysicalName, renamedPhysical, ct)).Should().Be(1);
    }

    [Fact]
    public async Task A_table_whose_rename_cannot_be_logged_keeps_its_name_and_can_be_renamed_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(SchemaManager(), Guid.NewGuid(), ct);
        var request = new UpdateTableRequest
        {
            TableId = table.TableId,
            LogicalName = "renamed_" + Guid.NewGuid().ToString("N")[..8],
            ExpectedVersion = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct),
        };

        var act = () => SchemaManager(new ThrowingChangeLogger(SchemaOperation.UpdateTable)).UpdateTableAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        var after = await Repository().GetTableByIdAsync(table.TableId, cancellationToken: ct);
        after!.LogicalName.Should().Be(table.LogicalName, "a rename that did not happen is not in the description");
        after.SchemaVersion.Should().Be(request.ExpectedVersion);

        var renamed = await SchemaManager().UpdateTableAsync(request, ct);
        renamed.LogicalName.Should().Be(request.LogicalName);
    }

    [Theory]
    [InlineData(false, "v")]
    [InlineData(true, "m")]
    public async Task A_view_whose_creation_cannot_be_logged_is_not_built_and_can_be_built_again(bool materialized, string kind)
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTableAsync(SchemaManager(), projectId, ct);
        var name = "view_" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateViewRequest
        {
            ProjectId = projectId,
            Name = name,
            IsMaterialized = materialized,
            Definition = new ViewDefinition { BaseTable = table.LogicalName, Columns = [new ViewColumnSpec { Source = "qty", Alias = "qty" }] },
        };

        var act = () => ViewManager(new ThrowingChangeLogger(SchemaOperation.CreateView)).CreateViewAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await RelationsNamedAsync(_hasher.GenerateViewName(projectId, name), kind, ct))
            .Should().Be(0, "the view a failed change built is taken back with it");
        (await new ViewMetadataRepository(_fixture.Connections).GetViewByNameAsync(projectId, name, ct)).Should().BeNull();

        var built = await ViewManager().CreateViewAsync(request, ct);
        built.LogicalName.Should().Be(name);
    }

    [Fact]
    public async Task A_view_whose_new_definition_cannot_be_logged_keeps_its_definition_and_can_be_changed_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTableAsync(SchemaManager(), projectId, ct, extra: new CreateColumnRequest
        {
            LogicalName = "note",
            DataType = MorphDataType.Text,
            IsNullable = true,
        });
        var view = await ViewManager().CreateViewAsync(new CreateViewRequest
        {
            ProjectId = projectId,
            Name = "view_" + Guid.NewGuid().ToString("N")[..8],
            Definition = new ViewDefinition { BaseTable = table.LogicalName, Columns = [new ViewColumnSpec { Source = "qty", Alias = "qty" }] },
        }, ct);
        var before = await ViewDefinitionAsync(view.PhysicalName, ct);
        var request = new UpdateViewRequest
        {
            ViewId = view.ViewId,
            Definition = new ViewDefinition
            {
                BaseTable = table.LogicalName,
                Columns = [new ViewColumnSpec { Source = "qty", Alias = "qty" }, new ViewColumnSpec { Source = "note", Alias = "note" }],
            },
        };

        var act = () => ViewManager(new ThrowingChangeLogger(SchemaOperation.UpdateView)).UpdateViewAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await ViewDefinitionAsync(view.PhysicalName, ct)).Should().Be(before, "a change that did not happen leaves the view as it was");

        await ViewManager().UpdateViewAsync(request, ct);
        (await ViewDefinitionAsync(view.PhysicalName, ct)).Should().NotBe(before);
    }

    [Fact]
    public async Task A_view_whose_deletion_cannot_be_logged_is_still_there_and_can_be_deleted_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var table = await CreateTableAsync(SchemaManager(), projectId, ct);
        var view = await ViewManager().CreateViewAsync(new CreateViewRequest
        {
            ProjectId = projectId,
            Name = "view_" + Guid.NewGuid().ToString("N")[..8],
            Definition = new ViewDefinition { BaseTable = table.LogicalName, Columns = [new ViewColumnSpec { Source = "qty", Alias = "qty" }] },
        }, ct);

        var act = () => ViewManager(new ThrowingChangeLogger(SchemaOperation.DeleteView)).DeleteViewAsync(view.ViewId, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        (await RelationsNamedAsync(view.PhysicalName, "v", ct)).Should().Be(1, "a deletion that did not happen leaves the view where it was");
        (await new ViewMetadataRepository(_fixture.Connections).GetViewByIdAsync(view.ViewId, ct)).Should().NotBeNull("and its description with it");

        await ViewManager().DeleteViewAsync(view.ViewId, ct);
        (await RelationsNamedAsync(view.PhysicalName, "v", ct)).Should().Be(0);
    }

    private static async Task<TableMetadata> CreateTableAsync(
        PostgresSchemaManager schema, Guid projectId, CancellationToken ct, CreateColumnRequest? extra = null)
    {
        List<CreateColumnRequest> columns = [new() { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }];
        if (extra is not null)
        {
            columns.Add(extra);
        }

        return await schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = "atomic_" + Guid.NewGuid().ToString("N")[..8],
            Columns = columns,
        }, ct);
    }

    private async Task<long> PhysicalColumnsAsync(string physicalTable, string physicalColumn, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid " +
            "WHERE c.relname = @Table AND a.attname = @Column AND NOT a.attisdropped",
            new { Table = physicalTable, Column = physicalColumn });
    }

    private async Task<long> RelationsNamedAsync(string physicalName, string kind, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE c.relkind = @Kind AND c.relname = @Name",
            new { Kind = kind, Name = physicalName });
    }

    private async Task<string?> ViewDefinitionAsync(string physicalView, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT pg_get_viewdef(c.oid) FROM pg_class c WHERE c.relname = @Name AND c.relkind = 'v'", new { Name = physicalView });
    }

    private PostgresViewManager ViewManager(IChangeLogger? changeLogger = null) => new(
        _fixture.Connections,
        new ViewMetadataRepository(_fixture.Connections),
        Repository(),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        _hasher,
        changeLogger ?? new ChangeLogger(_fixture.Connections));

    private async Task<string?> PhysicalTypeAsync(string physicalTable, string physicalColumn, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<string?>(
            "SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid " +
            "WHERE c.relname = @Table AND a.attname = @Column AND NOT a.attisdropped",
            new { Table = physicalTable, Column = physicalColumn });
    }

    private async Task<long> ConstraintsNamedAsync(string name, CancellationToken ct)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE conname = @Name", new { Name = name });
    }

    private MetadataRepository Repository() => new(_fixture.Connections);

    private PostgresSchemaManager SchemaManager(IChangeLogger? changeLogger = null) => new(
        _fixture.Connections,
        Repository(),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        _hasher,
        changeLogger ?? new ChangeLogger(_fixture.Connections),
        new ProjectRepository(_fixture.Connections, new PostgresSchemaNameResolver()),
        new SchemaManagerOptions());
}
