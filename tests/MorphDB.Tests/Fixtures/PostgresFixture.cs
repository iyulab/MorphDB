using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Npgsql.Schema;
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

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        DataSource = dataSourceBuilder.Build();

        // The production bootstrap is the only source of the global schema. This fixture used to
        // hand-copy the DDL, which meant no test ever executed the real bootstrap -- and the copy
        // silently diverged. It then called DdlBuilder directly, which was closer but still skipped
        // the pre-bootstrap migration step production runs first. Calling the same service method
        // start-up calls is what makes a broken bootstrap path turn tests red.
        var schemaLayer = new PostgresSchemaLayerService(
            DataSource,
            new PostgresSchemaNameResolver(),
            NullLogger<PostgresSchemaLayerService>.Instance);
        await schemaLayer.EnsureGlobalSchemaAsync();
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
