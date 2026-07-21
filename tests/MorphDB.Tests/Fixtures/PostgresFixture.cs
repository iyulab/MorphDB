using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Pipeline;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Diagnostics;
using MorphDB.Npgsql.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;
using MorphDB.Npgsql.Pipeline.Validators;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Services;
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

    /// <summary>
    /// Builds a <see cref="PostgresDataService"/> wired to the same write pipeline
    /// <c>AddMorphDbNpgsql</c> registers, so tests that construct services by hand exercise the
    /// pipeline production traffic goes through. Kept on the fixture (one place) precisely so the
    /// assembly cannot drift per test file; if the production registration list changes, change it
    /// here too.
    /// </summary>
    public PostgresDataService CreateDataService(
        IMetadataRepository metadataRepository,
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor)
    {
        var diagnostics = new QueryDiagnosticsService(
            Options.Create(new QueryDiagnosticsOptions()),
            NullLogger<QueryDiagnosticsService>.Instance);
        var executor = new PostgresWriteExecutor(DataSource, diagnostics);

        IValidator[] validators =
        [
            new UnknownFieldValidator(),
            new RequiredValidator(),
            new UniqueValidator(DataSource),
            new ForeignKeyValidator(DataSource, metadataRepository),
            new CheckValidator()
        ];
        ITransformer[] transformers =
        [
            new IdApplier(),
            new DefaultValueApplier(),
            new TimestampApplier(),
            new VersionApplier(),
            new AuditFieldApplier(),
            new OwnerApplier(),
            new SortOrderApplier(DataSource),
            new SoftDeleteApplier(),
            new RowStateApplier()
        ];

        var pipeline = new WritePipeline(validators, transformers, securityContextAccessor, executor);
        return new PostgresDataService(
            DataSource, metadataRepository, securityPolicyService, securityContextAccessor, pipeline);
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
