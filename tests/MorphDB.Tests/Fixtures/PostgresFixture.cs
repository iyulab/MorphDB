using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MorphDB.Core.Diagnostics;
using MorphDB.Core.Pipeline;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Diagnostics;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Pipeline;
using MorphDB.Npgsql.Pipeline.Transformers;
using MorphDB.Npgsql.Pipeline.Validators;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = TestPostgres.Create("morphdb_test");

    /// <summary>The service role's connection — what everything under test connects as.</summary>
    public string ConnectionString => TestPostgres.ServiceConnectionString(_container);

    /// <summary>
    /// The image's bootstrap superuser. Only for a test that needs a role MorphDB must refuse.
    /// </summary>
    public string SuperuserConnectionString => _container.GetConnectionString();

    /// <summary>For a test to look at the database directly; services under test open through <see cref="Connections"/>.</summary>
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    /// <summary>
    /// What a hand-constructed service opens its connections through — the same factory
    /// <c>AddMorphDbNpgsql</c> registers, over <see cref="DataSource"/>.
    /// </summary>
    public MorphConnectionFactory Connections { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        DataSource = dataSourceBuilder.Build();
        Connections = new MorphConnectionFactory(DataSource, new SecurityContextAccessor());

        // The production bootstrap is the only source of the global schema. This fixture used to
        // hand-copy the DDL, which meant no test ever executed the real bootstrap -- and the copy
        // silently diverged. It then called DdlBuilder directly, which was closer but still skipped
        // the pre-bootstrap migration step production runs first. Calling the same service method
        // start-up calls is what makes a broken bootstrap path turn tests red.
        var resolver = new PostgresSchemaNameResolver();
        var schemaLayer = new PostgresSchemaLayerService(
            Connections,
            resolver,
            new ProjectRepository(Connections, resolver),
            NullLogger<PostgresSchemaLayerService>.Instance);
        await schemaLayer.EnsureGlobalSchemaAsync();
    }

    public async ValueTask DisposeAsync()
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
        var executor = new PostgresWriteExecutor(Connections, diagnostics);

        IValidator[] validators =
        [
            new UnknownFieldValidator(),
            new RequiredValidator(),
            new UniqueValidator(Connections),
            new ForeignKeyValidator(Connections, metadataRepository),
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
            new SortOrderApplier(Connections),
            new SoftDeleteApplier(),
            new RowStateApplier()
        ];

        var pipeline = new WritePipeline(validators, transformers, securityContextAccessor, executor);
        return new PostgresDataService(
            Connections, metadataRepository, securityPolicyService, securityContextAccessor, pipeline);
    }
}

[CollectionDefinition("PostgreSQL")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
