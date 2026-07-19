using MorphDB.Npgsql.Ddl;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A throwaway PostgreSQL container initialised by exactly one thing: the production global DDL.
/// Nothing hand-written touches it, which is the whole point — a fixture that builds the schema
/// itself can stay green no matter what the bootstrap emits.
/// </summary>
public sealed class CleanDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;

    private CleanDatabase(PostgreSqlContainer container) => _container = container;

    public string ConnectionString => _container.GetConnectionString();

    public static async Task<CleanDatabase> EmptyAsync(string database)
    {
        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase(database)
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await container.StartAsync();
        return new CleanDatabase(container);
    }

    public static async Task<CleanDatabase> BootstrapAsync(string database)
    {
        var db = await EmptyAsync(database);
        await db.ExecuteAsync(DdlBuilder.BuildGlobalSystemSchemaDdl());
        return db;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> ReadTablesAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'morphdb'
            ORDER BY table_name
            """,
            connection);

        return await ReadStringsAsync(command);
    }

    public async Task<List<string>> ReadColumnsAsync(string table)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'morphdb' AND table_name = @table
            ORDER BY column_name
            """,
            connection);
        command.Parameters.AddWithValue("table", table);

        return await ReadStringsAsync(command);
    }

    private static async Task<List<string>> ReadStringsAsync(NpgsqlCommand command)
    {
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
