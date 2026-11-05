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
/// A table is created as a physical table first and its metadata after. When anything in the second half
/// fails, the caller sees that failure -- and must not be left with a physical table that nothing
/// describes, whose name could never be created again.
/// </summary>
[Collection("PostgreSQL")]
public class TableCreationRollbackTests
{
    private readonly PostgresFixture _fixture;

    public TableCreationRollbackTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_failure_after_the_physical_table_is_built_takes_it_down_and_the_name_can_be_created_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = Guid.NewGuid();
        var name = "rollback_" + Guid.NewGuid().ToString("N")[..8];
        var request = new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = name,
            Columns = [new CreateColumnRequest { LogicalName = "qty", DataType = MorphDataType.Integer, IsNullable = true }],
        };

        var failing = SchemaManager(new ThrowingChangeLogger());
        var act = () => failing.CreateTableAsync(request, ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("change log is down");

        await using (var connection = await _fixture.DataSource.OpenConnectionAsync(ct))
        {
            var leftover = await connection.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
                "WHERE c.relkind = 'r' AND c.relname = @Physical",
                new { Physical = new Sha256NameHasher().GenerateTableName(projectId, name) });
            leftover.Should().Be(0, "the physical table a failed creation built is taken down again");
        }

        var table = await SchemaManager(new ChangeLogger(_fixture.Connections)).CreateTableAsync(request, ct);
        table.LogicalName.Should().Be(name, "the name is free again");
    }

    private PostgresSchemaManager SchemaManager(IChangeLogger changeLogger)
    {
        var metadataRepository = new MetadataRepository(_fixture.Connections);
        return new PostgresSchemaManager(
            _fixture.Connections,
            metadataRepository,
            new PostgresAdvisoryLockManager(_fixture.Connections, new AdvisoryLockOptions()),
            new Sha256NameHasher(),
            changeLogger,
            new ProjectRepository(_fixture.Connections, new PostgresSchemaNameResolver()),
            new SchemaManagerOptions());
    }
}
