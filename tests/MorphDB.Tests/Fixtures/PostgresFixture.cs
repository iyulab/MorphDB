using MorphDB.Npgsql.Ddl;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("morphdb_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Initialize schema
        await InitializeSchemaAsync();

        // Create data source for tests
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        DataSource = dataSourceBuilder.Build();
    }

    private async Task InitializeSchemaAsync()
    {
        // The production bootstrap is the only source of the global schema. This fixture used to
        // hand-copy it, which meant no test ever executed the real DDL -- and the copy silently
        // diverged: it built six control-plane tables production never created, and omitted the
        // view and policy tables production code queries. Calling the builder is what makes a
        // broken bootstrap turn tests red.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(DdlBuilder.BuildGlobalSystemSchemaDdl(), connection);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        DataSource.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
