using System.Collections.Concurrent;
using System.Text;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Service.Services;
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
        Bulk = new PostgresBulkOperationService(
            fixture.Connections, Schema, Data, BulkCallers, new BulkOperationOptions(), pipeline);
        JobProcessor = new BulkJobProcessorService(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BulkJobProcessorService>.Instance);
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

    /// <summary>
    /// The accessor <see cref="Bulk"/> makes a job's requester ambient through. It is the ambient
    /// context every other service reads; what it adds is a record of each context set through it.
    /// </summary>
    public RecordingSecurityContextAccessor BulkCallers { get; } = new();

    /// <summary>
    /// The hosted service that carries jobs out. Its polling loop is not started: it would pick up
    /// every pending job in the database the tests share. A test hands it one job at a time, through
    /// the methods the loop calls for each job it finds.
    /// </summary>
    public BulkJobProcessorService JobProcessor { get; }
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
        new SecurityContextAccessor().SetContext(caller);
        return await action();
    }

    /// <inheritdoc cref="As{T}"/>
    public static async Task As(SecurityContext? caller, Func<Task> action)
    {
        new SecurityContextAccessor().SetContext(caller);
        await action();
    }

    /// <summary>The ambient security context where this is read, or <c>null</c>.</summary>
    public static SecurityContext? Ambient => new SecurityContextAccessor().ContextOrNull;

    /// <summary>
    /// A table whose rows carry an <c>owner</c> (text) and an <c>owner_id</c> (uuid), in a hierarchy
    /// and with row state — enough for every read path to have something to read.
    /// </summary>
    public Task<TableMetadata> CreateDocumentsAsync(Guid projectId, string prefix = "rls_docs", bool softDelete = false) =>
        Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)],
            SystemColumns = new SystemColumnOptions
            {
                HierarchyEnabled = true,
                RowStateEnabled = true,
                SoftDeleteEnabled = softDelete
            },
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

    /// <summary>
    /// Has the job processor carry out one export job, and answers the lines of the file it stored
    /// — the file a download of that job serves — without the header.
    /// </summary>
    public async Task<List<string>> RunExportJobAsync(BulkExportJob job)
    {
        await JobProcessor.ProcessExportJobAsync(Bulk, job, CancellationToken.None);

        var stored = await StoredExportFileAsync(job.JobId)
            ?? throw new InvalidOperationException($"Export job '{job.JobId}' stored no file; it failed.");

        return Encoding.UTF8.GetString(stored)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1)
            .ToList();
    }

    /// <summary>
    /// The file stored for an export job, read from its table as MorphDB itself — or <c>null</c>
    /// when none is stored. Not through the service: that answers a caller, and only the job's
    /// requester, where this is asked whoever is ambient.
    /// </summary>
    public async Task<byte[]?> StoredExportFileAsync(Guid jobId)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        return await connection.ExecuteScalarAsync<byte[]?>(
            "SELECT data FROM morphdb._morph_export_data WHERE job_id = @JobId", new { JobId = jobId });
    }

    /// <summary>Has the job processor carry out one import job, and answers the job as it ended.</summary>
    public async Task<BulkImportJob> RunImportJobAsync(BulkImportJob job)
    {
        await JobProcessor.ProcessImportJobAsync(Bulk, job, CancellationToken.None);

        // Read as a caller every job of the project is there for, whoever requested this one.
        return (await As(SecurityContext.Service(job.ProjectId), () => Bulk.GetImportJobAsync(job.ProjectId, job.JobId)))!;
    }

    /// <summary>The security context a job's row holds, as stored — <c>null</c> when it holds none.</summary>
    public async Task<string?> StoredRequesterAsync(string jobTable, Guid jobId)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        return await connection.ExecuteScalarAsync<string?>(
            $"SELECT requested_by::text FROM morphdb.{jobTable} WHERE job_id = @JobId", new { JobId = jobId });
    }

    /// <summary>
    /// Leaves a job's row as a version that did not record the requester stored it: with no
    /// security context.
    /// </summary>
    public Task ForgetRequesterAsync(string jobTable, Guid jobId) =>
        ExecuteAsSystemAsync($"UPDATE morphdb.{jobTable} SET requested_by = NULL WHERE job_id = '{jobId}'");

    public Task<SecurityPolicy> CreatePolicyAsync(
        TableMetadata table, string expression, string name = "policy", PolicyType type = PolicyType.Select) =>
        Policies.CreatePolicyAsync(table.ProjectId, new CreatePolicyRequest
        {
            Name = name,
            TableName = table.LogicalName,
            PolicyType = type,
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

    /// <summary>
    /// The native policies projected from the table's stored ones, as PostgreSQL's catalog holds
    /// them: the command each is for, whether it is restrictive, and which clauses it carries —
    /// <c>UPDATE restrictive USING WITH CHECK</c>, say. In name order.
    /// </summary>
    public async Task<List<string>> NativePoliciesAsync(TableMetadata table)
    {
        await using var connection = await _fixture.Connections.OpenSystemAsync();
        return (await connection.QueryAsync<string>(
            $"""
            SELECT CASE polcmd::text
                       WHEN 'r' THEN 'SELECT' WHEN 'a' THEN 'INSERT' WHEN 'w' THEN 'UPDATE' WHEN 'd' THEN 'DELETE'
                       ELSE 'ALL'
                   END
                   || CASE WHEN polpermissive THEN ' permissive' ELSE ' restrictive' END
                   || CASE WHEN polqual IS NOT NULL THEN ' USING' ELSE '' END
                   || CASE WHEN polwithcheck IS NOT NULL THEN ' WITH CHECK' ELSE '' END
            FROM pg_policy
            WHERE polrelid = to_regclass(@Relation) AND starts_with(polname::text, '{NativePolicyProjector.PolicyNamePrefix}')
            ORDER BY polname
            """,
            new { Relation = DdlBuilder.QuoteIdentifier(table.PhysicalName) })).AsList();
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

    /// <summary>
    /// Runs a data statement past every policy — as the role a bypassing caller is switched to — for
    /// rows a test has to put in a state no caller could. Not for DDL: that role may create nothing.
    /// </summary>
    public async Task ExecutePastPoliciesAsync(string sql)
    {
        await using var connection = await OpenPastPoliciesAsync();
        await connection.ExecuteAsync(sql);
    }

    /// <summary>One value read past every policy and every filter a read applies.</summary>
    public async Task<T?> ScalarAsSystemAsync<T>(string sql)
    {
        await using var connection = await OpenPastPoliciesAsync();
        return await connection.ExecuteScalarAsync<T>(sql);
    }

    /// <summary>
    /// PostgreSQL's plan for <paramref name="sql"/> on a connection opened for <paramref name="caller"/>,
    /// with sequential scans priced out — so a condition PostgreSQL can answer from an index shows up
    /// as an index condition, and one it cannot is still a sequential scan.
    /// </summary>
    public Task<string> PlanAsync(SecurityContext? caller, string sql) => As(caller, async () =>
    {
        await using var connection = await _fixture.Connections.OpenAsync();
        await connection.ExecuteAsync("SET enable_seqscan = off");
        var lines = await connection.QueryAsync<string>($"EXPLAIN (COSTS OFF) {sql}");
        return string.Join('\n', lines);
    });

    /// <summary>
    /// One value read on a connection that never went through the factory — the login role with no
    /// context, as a tool pointed at the database with MorphDB's credentials would be.
    /// </summary>
    public async Task<T?> ScalarAsLoginWithoutContextAsync<T>(string sql)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql);
    }

    private async Task<NpgsqlConnection> OpenPastPoliciesAsync()
    {
        var connection = await _fixture.Connections.OpenSystemAsync();
        await connection.ExecuteAsync("SELECT set_config('role', @Role, false)", new { Role = _fixture.Connections.BypassRole });
        return connection;
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

    /// <summary>
    /// The ambient security context, with a record of every context set through this instance —
    /// in order, <c>null</c> for a context that was cleared.
    /// </summary>
    internal sealed class RecordingSecurityContextAccessor : ISecurityContextAccessor
    {
        private readonly SecurityContextAccessor _ambient = new();
        private readonly ConcurrentQueue<SecurityContext?> _changes = new();

        public SecurityContext Context => _ambient.Context;

        public SecurityContext? ContextOrNull => _ambient.ContextOrNull;

        public IReadOnlyList<SecurityContext?> Changes => [.. _changes];

        public void SetContext(SecurityContext? context)
        {
            _changes.Enqueue(context);
            _ambient.SetContext(context);
        }
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
