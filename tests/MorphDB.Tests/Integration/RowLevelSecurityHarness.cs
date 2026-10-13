using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;
using Npgsql;

namespace MorphDB.Tests.Integration;

/// <summary>
/// Everything the row-level security tests reach a table through, built the way
/// <c>AddMorphDbNpgsql</c> builds it, plus the two things those tests keep needing: a caller to run
/// as, and a look at what PostgreSQL itself holds for a table.
/// </summary>
internal sealed class RowLevelSecurityHarness
{
    public const string UserA = "user-a";
    public const string UserB = "user-b";

    private readonly PostgresFixture _fixture;

    public RowLevelSecurityHarness(PostgresFixture fixture)
    {
        _fixture = fixture;
        Metadata = new MetadataRepository(fixture.Connections);

        var locks = new PostgresAdvisoryLockManager(fixture.Connections, new AdvisoryLockOptions());
        var hasher = new Sha256NameHasher();
        var changes = new ChangeLogger(fixture.Connections);
        var accessor = new SecurityContextAccessor();

        Schema = new PostgresSchemaManager(
            fixture.Connections,
            Metadata,
            locks,
            hasher,
            changes,
            new ProjectRepository(fixture.Connections, new PostgresSchemaNameResolver()),
            new SchemaManagerOptions());

        Policies = new SecurityPolicyService(fixture.Connections, Metadata);

        var pipeline = fixture.CreateWritePipeline(Metadata, accessor);
        Data = new PostgresDataService(
            fixture.Connections,
            Metadata,
            pipeline,
            new PostgresLookupResolver(Metadata),
            new PostgresRollupResolver(Metadata),
            new PostgresFormulaResolver());

        Aggregation = new PostgresAggregationService(fixture.Connections, Metadata);
        Hierarchy = new PostgresHierarchyQueryService(fixture.Connections, Metadata);
        Views = new PostgresViewManager(
            fixture.Connections, new ViewMetadataRepository(fixture.Connections), Metadata, locks, hasher, changes);
        Bulk = new PostgresBulkOperationService(fixture.Connections, Schema, Data, new BulkOperationOptions(), pipeline);
        Transactions = new PostgresTransactionService(fixture.Connections, Metadata, pipeline);
    }

    public MetadataRepository Metadata { get; }
    public PostgresSchemaManager Schema { get; }
    public SecurityPolicyService Policies { get; }
    public PostgresDataService Data { get; }
    public PostgresAggregationService Aggregation { get; }
    public PostgresHierarchyQueryService Hierarchy { get; }
    public PostgresViewManager Views { get; }
    public PostgresBulkOperationService Bulk { get; }
    public PostgresTransactionService Transactions { get; }

    /// <summary>A caller with an identity, subject to every policy.</summary>
    public static SecurityContext User(Guid projectId, string userId) =>
        new() { ProjectId = projectId, UserId = userId, IsAuthenticated = true };

    /// <summary>
    /// Runs <paramref name="action"/> as <paramref name="caller"/> — <c>null</c> for no ambient
    /// context at all. The context is ambient only for the action: this method is asynchronous, so
    /// what it sets does not flow back to the test that called it.
    /// </summary>
    public static async Task<T> As<T>(SecurityContext? caller, Func<Task<T>> action)
    {
        new SecurityContextAccessor().SetContext(caller!);
        return await action();
    }

    /// <inheritdoc cref="As{T}"/>
    public static async Task As(SecurityContext? caller, Func<Task> action)
    {
        new SecurityContextAccessor().SetContext(caller!);
        await action();
    }

    /// <summary>
    /// A table whose rows carry an <c>owner</c> (text) and an <c>owner_id</c> (uuid), in a hierarchy
    /// and with row state — enough for every read path to have something to read.
    /// </summary>
    public Task<TableMetadata> CreateDocumentsAsync(Guid projectId, string prefix = "rls_docs") =>
        Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)],
            SystemColumns = new SystemColumnOptions { HierarchyEnabled = true, RowStateEnabled = true },
            Columns =
            [
                new CreateColumnRequest { LogicalName = "title", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "owner", DataType = MorphDataType.Text, IsNullable = true },
                new CreateColumnRequest { LogicalName = "owner_id", DataType = MorphDataType.Uuid, IsNullable = true },
                new CreateColumnRequest { LogicalName = "department", DataType = MorphDataType.Text, IsNullable = true },
                new CreateColumnRequest { LogicalName = "folder_id", DataType = MorphDataType.Uuid, IsNullable = true },
                new CreateColumnRequest { LogicalName = "scratch", DataType = MorphDataType.Text, IsNullable = true },
            ]
        });

    /// <summary>Inserts a row as a caller no policy applies to, and answers its id.</summary>
    public Task<Guid> SeedAsync(TableMetadata table, IDictionary<string, object?> row) =>
        As(SecurityContext.Service(table.ProjectId), async () =>
        {
            var inserted = await Data.InsertAsync(table.ProjectId, table.LogicalName, row);
            return (Guid)inserted[SystemColumns.Id]!;
        });

    /// <summary>The row as it is stored, read past every policy — or <c>null</c> when it is gone.</summary>
    public Task<IDictionary<string, object?>?> StoredAsync(TableMetadata table, Guid id) =>
        As(SecurityContext.Service(table.ProjectId), () => Data.GetByIdAsync(table.ProjectId, table.LogicalName, id));

    public Task<SecurityPolicy> CreatePolicyAsync(TableMetadata table, string expression, string name = "policy") =>
        Policies.CreatePolicyAsync(table.ProjectId, new CreatePolicyRequest
        {
            Name = name,
            TableName = table.LogicalName,
            PolicyType = PolicyType.Select,
            Expression = expression
        });

    /// <summary>
    /// A policy row written straight to the table, as a version that did not project policies would
    /// have left it: stored, and unknown to PostgreSQL.
    /// </summary>
    public async Task<Guid> StoreUnprojectedPolicyAsync(
        TableMetadata table, string expression, string name = "stored_earlier", PolicyType type = PolicyType.Select)
    {
        var id = Guid.NewGuid();
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_security_policies
            (id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at)
            VALUES (@Id, @ProjectId, @TableId, @Name, NULL, @PolicyType, @Expression, true, 1, NOW(), NOW())
            """,
            new { Id = id, table.ProjectId, table.TableId, Name = name, PolicyType = (int)type, Expression = expression });
        return id;
    }

    /// <summary>The start-up entry point: what a host runs before it serves anything.</summary>
    public Task StartUpAsync(ILogger<PostgresSchemaLayerService> logger)
    {
        var resolver = new PostgresSchemaNameResolver();
        return new PostgresSchemaLayerService(
            _fixture.Connections,
            resolver,
            new ProjectRepository(_fixture.Connections, resolver),
            logger).EnsureGlobalSchemaAsync();
    }

    /// <summary>What PostgreSQL holds for the table: its policies, and whether row security is on.</summary>
    public async Task<NativeState> NativeStateAsync(TableMetadata table)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        var relation = DdlBuilder.QuoteIdentifier(table.PhysicalName);

        var policies = (await connection.QueryAsync<(string Name, string Version)>(
            "SELECT polname::text, xmin::text FROM pg_policy WHERE polrelid = to_regclass(@Relation) ORDER BY polname",
            new { Relation = relation })).ToList();

        var flags = await connection.QuerySingleAsync<(bool Enabled, bool Forced)>(
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@Relation)",
            new { Relation = relation });

        return new NativeState(
            policies.Select(p => p.Name).ToList(),
            policies.Select(p => p.Version).ToList(),
            flags.Enabled,
            flags.Forced);
    }

    /// <summary>Whether PostgreSQL has a table or view of this physical name.</summary>
    public async Task<bool> RelationExistsAsync(string physicalName)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        return await connection.ExecuteScalarAsync<bool>(
            "SELECT to_regclass(@Relation) IS NOT NULL",
            new { Relation = DdlBuilder.QuoteIdentifier(physicalName) });
    }

    /// <summary>
    /// Runs a statement as MorphDB itself — for the state only an earlier version, or SQL written
    /// by hand, could have left behind.
    /// </summary>
    public async Task ExecuteAsSystemAsync(string sql)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        await connection.ExecuteAsync(sql);
    }

    /// <summary>A connection of MorphDB's own, for a test that has to hold a transaction open.</summary>
    public Task<NpgsqlConnection> OpenSystemConnectionAsync() => _fixture.Connections.OpenSystemAsync();

    /// <summary>
    /// Returns once some statement is waiting for a lock on the table — the moment at which the
    /// transaction it waits for can be committed with the order of the two settled.
    /// </summary>
    public async Task WaitUntilALockOnIsAwaitedAsync(TableMetadata table)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var waiting = await connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE relation = to_regclass(@Relation) AND NOT granted)",
                new { Relation = DdlBuilder.QuoteIdentifier(table.PhysicalName) });
            if (waiting)
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Nothing came to wait for a lock on table '{table.LogicalName}'.");
    }

    /// <summary>The number of stored policy rows the table has.</summary>
    public async Task<int> StoredPolicyCountAsync(TableMetadata table)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM morphdb._morph_security_policies WHERE table_id = @TableId",
            new { table.TableId });
    }

    /// <param name="Names">Native policy names, sorted.</param>
    /// <param name="Versions">
    /// The <c>xmin</c> of each policy's catalog row, in the same order. It changes when the row is
    /// written, so equal versions before and after mean PostgreSQL was asked to change nothing.
    /// </param>
    internal sealed record NativeState(
        IReadOnlyList<string> Names, IReadOnlyList<string> Versions, bool Enabled, bool Forced)
    {
        public IEnumerable<string> ProjectedNames => Names.Where(n => n.StartsWith("morphdb_", StringComparison.Ordinal));
    }
}
