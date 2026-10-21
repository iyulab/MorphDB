using Dapper;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A connection that keeps its statements prepared plans each against a table's columns as they were, and
/// PostgreSQL refuses to run one whose result changed shape ("cached plan must not change result type").
/// The pool forgets its prepared statements whenever the definition cache is cleared, so a table that
/// gained a column is read with it, not failed once per connection.
/// </summary>
[Collection("PostgreSQL")]
public class PreparedStatementInvalidationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_prepared_read_fails_once_after_a_column_is_added_unless_the_pool_forgets_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (dataSource, table) = await PreparedReadAsync(ct);
        await using (dataSource)
        {
            await AddColumnAsync(dataSource, table, ct);

            var stale = () => ReadColumnsAsync(dataSource, table, ct);
            (await stale.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.FeatureNotSupported,
                "this is the failure the pool's forgetting exists to prevent; if PostgreSQL stops raising it, the hook can go");
        }
    }

    [Fact]
    public async Task A_cleared_definition_cache_makes_the_pool_forget_its_prepared_statements()
    {
        var ct = TestContext.Current.CancellationToken;
        var (dataSource, table) = await PreparedReadAsync(ct);
        await using (dataSource)
        {
            var cache = new MetadataCache();
            cache.Cleared += dataSource.Clear;
            await AddColumnAsync(dataSource, table, ct);

            cache.Invalidate(Guid.NewGuid());

            (await ReadColumnsAsync(dataSource, table, ct)).Should().Be(2, "the read is prepared again, against the table as it is now");
        }
    }

    /// <summary>A pool of one connection that has prepared <c>SELECT *</c> over a new one-column table.</summary>
    private async Task<(NpgsqlDataSource DataSource, string Table)> PreparedReadAsync(CancellationToken ct)
    {
        var dataSource = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            MaxPoolSize = 1,
            MaxAutoPrepare = 10,
            AutoPrepareMinUsages = 2,
        });
        var table = $"prepared_{Guid.NewGuid():N}";
        await using (var connection = await dataSource.OpenConnectionAsync(ct))
        {
            await connection.ExecuteAsync($"CREATE TABLE {table} (a int); INSERT INTO {table} VALUES (1)");
        }

        for (var i = 0; i < 3; i++)
        {
            (await ReadColumnsAsync(dataSource, table, ct)).Should().Be(1);
        }

        return (dataSource, table);
    }

    private static async Task AddColumnAsync(NpgsqlDataSource dataSource, string table, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN b int");
    }

    private static async Task<int> ReadColumnsAsync(NpgsqlDataSource dataSource, string table, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var row = await connection.QuerySingleAsync(new CommandDefinition($"SELECT * FROM {table} WHERE a = @a", new { a = 1 }, cancellationToken: ct));
        return ((IDictionary<string, object>)row).Count;
    }
}
