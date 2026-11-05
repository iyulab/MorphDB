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
/// A caller that sends the version of the schema it saw is saying "change this only if nothing else has".
/// Two requests that saw the same version cannot both be right: whichever commits second must be refused,
/// however close behind the first it started.
/// </summary>
[Collection("PostgreSQL")]
public class SchemaVersionRaceTests
{
    private readonly PostgresFixture _fixture;

    public SchemaVersionRaceTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Of_two_table_renames_that_saw_the_same_version_one_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);
        var version = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct);

        var (first, second) = await RaceAsync(
            manager => manager.UpdateTableAsync(new UpdateTableRequest { TableId = table.TableId, LogicalName = "first_" + Suffix(), ExpectedVersion = version }, ct),
            manager => manager.UpdateTableAsync(new UpdateTableRequest { TableId = table.TableId, LogicalName = "second_" + Suffix(), ExpectedVersion = version }, ct),
            ct);

        first.Should().BeNull("the request that was first through is applied");
        second.Should().BeOfType<SchemaVersionConflictException>("the other saw a schema that is no longer there");
    }

    [Fact]
    public async Task Of_two_column_updates_that_saw_the_same_version_one_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);
        var qty = table.Columns.Single(c => c.LogicalName == "qty");
        var version = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct);

        var (first, second) = await RaceAsync(
            manager => manager.UpdateColumnAsync(new UpdateColumnRequest { ColumnId = qty.ColumnId, LogicalName = "first_" + Suffix(), ExpectedVersion = version }, ct),
            manager => manager.UpdateColumnAsync(new UpdateColumnRequest { ColumnId = qty.ColumnId, LogicalName = "second_" + Suffix(), ExpectedVersion = version }, ct),
            ct);

        first.Should().BeNull();
        second.Should().BeOfType<SchemaVersionConflictException>();
    }

    [Fact]
    public async Task Of_two_column_renames_that_saw_the_same_version_one_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await CreateTableAsync(ct);
        var qty = table.Columns.Single(c => c.LogicalName == "qty");
        var version = await Repository().GetCurrentVersionAsync(table.TableId, cancellationToken: ct);

        var (first, second) = await RaceAsync(
            manager => manager.RenameColumnAsync(new RenameColumnRequest { ColumnId = qty.ColumnId, NewLogicalName = "first_" + Suffix(), ExpectedVersion = version }, ct),
            manager => manager.RenameColumnAsync(new RenameColumnRequest { ColumnId = qty.ColumnId, NewLogicalName = "second_" + Suffix(), ExpectedVersion = version }, ct),
            ct);

        first.Should().BeNull();
        second.Should().BeOfType<SchemaVersionConflictException>();
    }

    /// <summary>
    /// Starts <paramref name="first"/> and holds it open -- change made, not yet committed -- starts
    /// <paramref name="second"/> behind it, gives it time to get as far as it can, then lets the first finish.
    /// </summary>
    private async Task<(Exception? First, Exception? Second)> RaceAsync(
        Func<PostgresSchemaManager, Task> first,
        Func<PostgresSchemaManager, Task> second,
        CancellationToken ct)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = first(SchemaManager(new GatedChangeLogger(new ChangeLogger(_fixture.Connections), reached, gate.Task)));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        var secondTask = second(SchemaManager());
        await Task.Delay(1500, ct);
        gate.SetResult();

        return (await CaptureAsync(firstTask), await CaptureAsync(secondTask));
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private async Task<TableMetadata> CreateTableAsync(CancellationToken ct) =>
        await SchemaManager().CreateTableAsync(new CreateTableRequest
        {
            ProjectId = Guid.NewGuid(),
            LogicalName = "race_" + Suffix(),
            Columns = [new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }],
        }, ct);

    private MetadataRepository Repository() => new(_fixture.Connections);

    private PostgresSchemaManager SchemaManager(IChangeLogger? changeLogger = null) => new(
        _fixture.Connections,
        Repository(),
        new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
        new Sha256NameHasher(),
        changeLogger ?? new ChangeLogger(_fixture.Connections),
        new ProjectRepository(_fixture.Connections, new PostgresSchemaNameResolver()),
        new SchemaManagerOptions());
}
