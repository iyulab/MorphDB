using System.Globalization;
using System.Text;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A <c>Select</c> policy is enforced by PostgreSQL, so a row it hides is hidden from every
/// statement — not only from the three reads that used to append the policy to their own WHERE
/// clause. Each test here reads or writes one table through one path as four callers: the owner of
/// some rows, the owner of the others, nobody (no ambient context), and a caller that bypasses
/// row-level security.
/// </summary>
[Collection("PostgreSQL")]
public class NativeRowLevelSecurityTests
{
    private readonly RowLevelSecurityHarness _rls;

    public NativeRowLevelSecurityTests(PostgresFixture fixture)
    {
        _rls = new RowLevelSecurityHarness(fixture);
    }

    /// <summary>
    /// Two rows each for A and B. B's second row hangs under A's first, so a walk down A's tree
    /// meets a row A may not read. All four are filed in one folder — an id only, until a test
    /// gives it a table.
    /// </summary>
    private sealed record Documents(
        TableMetadata Table, Guid Folder, Guid RootA, Guid ChildA, Guid RootB, Guid ChildBUnderA)
    {
        public Guid ProjectId => Table.ProjectId;
        public string Name => Table.LogicalName;
        public SecurityContext A => User(ProjectId, UserA);
        public SecurityContext B => User(ProjectId, UserB);
        public SecurityContext Bypass => SecurityContext.Service(ProjectId);

        public string TitleOf(Guid id) =>
            id == RootA ? "root-a" : id == ChildA ? "child-a" : id == RootB ? "root-b" : "child-b";

        /// <summary>Each caller, and the rows it is allowed to read.</summary>
        public IEnumerable<(string Who, SecurityContext? Caller, Guid[] Visible)> Callers =>
        [
            ("A", A, [RootA, ChildA]),
            ("B", B, [RootB, ChildBUnderA]),
            ("no caller", null, []),
            ("bypass", Bypass, [RootA, ChildA, RootB, ChildBUnderA]),
        ];
    }

    private async Task<Documents> CreateDocumentsWithOwnerPolicyAsync()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        var folder = Guid.NewGuid();

        var rootA = await _rls.SeedAsync(table, Row("root-a", UserA, folder: folder));
        var childA = await _rls.SeedAsync(table, Row("child-a", UserA, parent: rootA, folder: folder));
        var rootB = await _rls.SeedAsync(table, Row("root-b", UserB, folder: folder));
        var childB = await _rls.SeedAsync(table, Row("child-b", UserB, parent: rootA, folder: folder));

        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");
        return new Documents(table, folder, rootA, childA, rootB, childB);
    }

    private static Dictionary<string, object?> Row(string title, string owner, Guid? parent = null, Guid? folder = null) =>
        new() { ["title"] = title, ["owner"] = owner, [SystemColumns.ParentId] = parent, ["folder_id"] = folder };

    private static Guid IdOf(IDictionary<string, object?> row) => (Guid)row[SystemColumns.Id]!;

    // ---- reads --------------------------------------------------------------------------------

    [Fact]
    public async Task A_list_returns_only_the_rows_the_caller_may_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var rows = await As(caller, () => _rls.Data.Query(docs.ProjectId).From(docs.Name).SelectAll().ToListAsync());
            rows.Select(IdOf).Should().BeEquivalentTo(visible, who);
        }
    }

    [Fact]
    public async Task A_single_record_read_answers_nothing_for_a_hidden_row()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        foreach (var (who, caller, visible) in docs.Callers)
        {
            foreach (var id in new[] { docs.RootA, docs.RootB })
            {
                var row = await As(caller, () => _rls.Data.GetByIdAsync(docs.ProjectId, docs.Name, id));
                (row is not null).Should().Be(visible.Contains(id), $"{who} reads {id}");
            }
        }
    }

    /// <summary>
    /// Finalizing a record reads it first with a statement of its own, which no policy was ever
    /// appended to.
    /// </summary>
    [Fact]
    public async Task The_transaction_services_own_record_read_does_not_find_a_hidden_row()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var result = await As(caller, () => _rls.Transactions.FinalizeAsync(docs.ProjectId, docs.Name, docs.RootB));

            if (visible.Contains(docs.RootB))
            {
                result.Success.Should().BeTrue(who);
            }
            else
            {
                result.Success.Should().BeFalse(who);
                result.Errors.Should().ContainSingle(e => e.Error == "not_found", who);
            }
        }
    }

    [Fact]
    public async Task A_count_counts_only_the_rows_the_caller_may_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var request = new AggregationRequest
        {
            Aggregations = [new AggregationColumn { Function = AggregateFunction.Count, Alias = "n" }]
        };

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var result = await As(caller, () => _rls.Aggregation.AggregateAsync(docs.ProjectId, docs.Name, request));
            Convert.ToInt64(result.Data[0]["n"], CultureInfo.InvariantCulture).Should().Be(visible.Length, who);

            var counted = await As(caller, () => _rls.Data.Query(docs.ProjectId).From(docs.Name).CountAsync());
            counted.Should().Be(visible.Length, who);
        }
    }

    [Fact]
    public async Task A_walk_down_a_hierarchy_does_not_pass_through_a_hidden_row()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var request = new HierarchyQueryRequest
        {
            ProjectId = docs.ProjectId,
            TableName = docs.Name,
            ParentColumn = SystemColumns.ParentId,
            RecordId = docs.RootA
        };

        // Both children hang under A's root; each caller is given the one it may read.
        foreach (var (who, caller, visible) in docs.Callers)
        {
            var result = await As(caller, () => _rls.Hierarchy.GetDescendantsAsync(request));
            result.Records.Select(r => (Guid)r.Data[SystemColumns.Id]!)
                .Should().BeEquivalentTo(visible.Intersect([docs.ChildA, docs.ChildBUnderA]), who);
        }

        // And upwards: B's child has A's root for a parent, which B may not read.
        var ancestors = await As(docs.B, () => _rls.Hierarchy.GetAncestorsAsync(request with { RecordId = docs.ChildBUnderA }));
        ancestors.Records.Should().BeEmpty("the only ancestor is a row B may not read");
    }

    [Fact]
    public async Task A_view_over_the_table_shows_only_the_rows_the_caller_may_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var viewName = "rls_view_" + Guid.NewGuid().ToString("N")[..8];
        await _rls.Views.CreateViewAsync(new CreateViewRequest
        {
            ProjectId = docs.ProjectId,
            Name = viewName,
            Definition = new ViewDefinition
            {
                BaseTable = docs.Name,
                Columns = [new ViewColumnSpec { Source = "title", Alias = "title" }]
            }
        }, TestContext.Current.CancellationToken);

        var titles = new Dictionary<Guid, string>
        {
            [docs.RootA] = "root-a",
            [docs.ChildA] = "child-a",
            [docs.RootB] = "root-b",
            [docs.ChildBUnderA] = "child-b"
        };

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var result = await As(caller, () => _rls.Views.QueryViewAsync(
                new ViewQueryRequest { ProjectId = docs.ProjectId, ViewName = viewName }));

            result.Data.Select(r => (string)r["title"]!).Should().BeEquivalentTo(visible.Select(id => titles[id]), who);
            result.TotalCount.Should().Be(visible.Length, who);
        }
    }

    // ---- the shape of the policy --------------------------------------------------------------

    /// <summary>
    /// The policy's condition is a plain comparison PostgreSQL can answer from an index on the
    /// column. When the policy carried its own bypass check, the check wrapped the comparison and
    /// every read of a policy table scanned it whole — about 140 times slower over 200,000 rows.
    /// </summary>
    [Fact]
    public async Task A_policy_condition_over_an_indexed_column_is_answered_from_the_index()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var table = docs.Table.PhysicalName;
        var owner = docs.Table.Columns.Single(c => c.LogicalName == "owner").PhysicalName;
        await _rls.ExecuteAsSystemAsync($"""CREATE INDEX "ix_{table}_owner" ON "{table}" ("{owner}")""");

        var plan = await _rls.PlanAsync(docs.A, $"""SELECT count(*) FROM "{table}" """);

        plan.Should().Contain("Index Cond").And.Contain(owner,
            "the policy's comparison is the index condition, not a filter over every row");
    }

    /// <summary>
    /// A connection that never went through MorphDB — a tool given MorphDB's credentials — is the
    /// role the policies restrict, with nobody in its context, and sees none of the rows a policy guards.
    /// </summary>
    [Fact]
    public async Task A_connection_that_did_not_come_from_MorphDB_sees_none_of_the_guarded_rows()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        var count = await _rls.ScalarAsLoginWithoutContextAsync<long>($"""SELECT count(*) FROM "{docs.Table.PhysicalName}" """);

        count.Should().Be(0);
    }

    private static CreateViewRequest TitlesView(Guid projectId, string viewName, string tableName, bool materialized) => new()
    {
        ProjectId = projectId,
        Name = viewName,
        IsMaterialized = materialized,
        Definition = new ViewDefinition
        {
            BaseTable = tableName,
            Columns = [new ViewColumnSpec { Source = "title", Alias = "title" }]
        }
    };

    /// <summary>
    /// A policy for writes alone does not narrow what a caller reads — every row of the table is
    /// read directly — so a materialized view over the table hands out nothing more, and is built.
    /// A policy for every command narrows reads, and the view is still refused.
    /// </summary>
    [Fact]
    public async Task A_materialized_view_is_built_over_a_table_whose_policies_only_bind_writes()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, Row("root-a", UserA));
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "inserts", PolicyType.Insert);
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "updates", PolicyType.Update);
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "deletes", PolicyType.Delete);
        var viewName = "rls_mwrite_" + Guid.NewGuid().ToString("N")[..8];

        var view = await _rls.Views.CreateViewAsync(TitlesView(table.ProjectId, viewName, table.LogicalName, materialized: true), TestContext.Current.CancellationToken);

        view.Should().NotBeNull();

        var other = await _rls.CreateDocumentsAsync(table.ProjectId, "rls_all");
        await _rls.CreatePolicyAsync(other, "owner = {{user_id}}", "everything", PolicyType.All);
        var refused = () => _rls.Views.CreateViewAsync(
            TitlesView(other.ProjectId, "rls_mall_" + Guid.NewGuid().ToString("N")[..8], other.LogicalName, materialized: true));

        (await refused.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_OPERATION");
    }

    /// <summary>
    /// A materialized view is a table of its own, filled when it is created or refreshed. PostgreSQL
    /// applies the base table's policies to the statement that fills it — which MorphDB runs as
    /// itself — and none to the copy, so every caller would read every row. It is not built.
    /// </summary>
    [Fact]
    public async Task A_materialized_view_cannot_be_built_over_a_table_that_has_a_policy()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var viewName = "rls_mview_" + Guid.NewGuid().ToString("N")[..8];

        var act = () => _rls.Views.CreateViewAsync(TitlesView(docs.ProjectId, viewName, docs.Name, materialized: true));

        var refusal = (await act.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("INVALID_OPERATION");
        refusal.Message.Should().Contain($"'{viewName}'").And.Contain($"'{docs.Name}'").And.Contain("'policy'")
            .And.Contain("row-level security policy").And.Contain("plain");
        (await _rls.Views.GetViewAsync(docs.ProjectId, viewName, TestContext.Current.CancellationToken)).Should().BeNull();
        (await _rls.RelationExistsAsync(new Sha256NameHasher().GenerateViewName(docs.ProjectId, viewName)))
            .Should().BeFalse("a refused view leaves nothing behind");

        // Nor one that only joins the table: it is what the view reads that counts, not its base.
        var notes = await _rls.Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = docs.ProjectId,
            LogicalName = "rls_notes_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "label", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "document_id", DataType = MorphDataType.Uuid, IsNullable = true },
            ]
        }, TestContext.Current.CancellationToken);
        var joinedName = "rls_mjoin_" + Guid.NewGuid().ToString("N")[..8];
        var joined = () => _rls.Views.CreateViewAsync(new CreateViewRequest
        {
            ProjectId = docs.ProjectId,
            Name = joinedName,
            IsMaterialized = true,
            Definition = new ViewDefinition
            {
                BaseTable = notes.LogicalName,
                Joins =
                [
                    new ViewJoinSpec
                    {
                        Table = docs.Name,
                        JoinType = ViewJoinType.Left,
                        Condition = $"{notes.LogicalName}.document_id = {docs.Name}._id"
                    }
                ],
                Columns = [new ViewColumnSpec { Source = "label", Alias = "label" }]
            }
        });
        (await joined.Should().ThrowAsync<SchemaException>()).Which.Message.Should().Contain($"'{docs.Name}'");

        // The same view, not materialized, is built — and read through the policy.
        await _rls.Views.CreateViewAsync(
            TitlesView(docs.ProjectId, viewName, docs.Name, materialized: false), TestContext.Current.CancellationToken);
        foreach (var (who, caller, visible) in docs.Callers)
        {
            var result = await As(caller, () => _rls.Views.QueryViewAsync(
                new ViewQueryRequest { ProjectId = docs.ProjectId, ViewName = viewName }));
            result.TotalCount.Should().Be(visible.Length, who);
        }
    }

    /// <summary>
    /// Redefining a materialized view builds it again, so the same rule holds — and a definition
    /// that is refused leaves the view that was there, with the index a concurrent refresh needs.
    /// </summary>
    [Fact]
    public async Task A_materialized_view_cannot_be_redefined_to_read_a_table_that_has_a_policy()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var open = await _rls.CreateDocumentsAsync(docs.ProjectId, "rls_open");
        await _rls.SeedAsync(open, Row("open-1", UserA));
        await _rls.SeedAsync(open, Row("open-2", UserB));

        static ViewDefinition Over(string tableName, int? limit = null) => new()
        {
            BaseTable = tableName,
            Columns =
            [
                new ViewColumnSpec { Source = SystemColumns.Id, Alias = SystemColumns.Id },
                new ViewColumnSpec { Source = "title", Alias = "title" }
            ],
            Limit = limit
        };

        var viewName = "rls_mview_" + Guid.NewGuid().ToString("N")[..8];
        var view = await _rls.Views.CreateViewAsync(new CreateViewRequest
        {
            ProjectId = docs.ProjectId,
            Name = viewName,
            IsMaterialized = true,
            Definition = Over(open.LogicalName)
        }, TestContext.Current.CancellationToken);

        Task<ViewQueryResult> ReadAsync() => As(docs.A, () => _rls.Views.QueryViewAsync(
            new ViewQueryRequest { ProjectId = docs.ProjectId, ViewName = viewName }));

        var redefine = () => _rls.Views.UpdateViewAsync(
            new UpdateViewRequest { ViewId = view.ViewId, Definition = Over(docs.Name) });

        (await redefine.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_OPERATION");
        (await ReadAsync()).Data.Select(r => (string)r["title"]!).Should().BeEquivalentTo(["open-1", "open-2"]);
        await _rls.Views.RefreshMaterializedViewAsync(view.ViewId, concurrent: true, TestContext.Current.CancellationToken);

        // A definition that reads no such table replaces the view, index included.
        await _rls.Views.UpdateViewAsync(
            new UpdateViewRequest { ViewId = view.ViewId, Definition = Over(open.LogicalName, limit: 1) },
            TestContext.Current.CancellationToken);
        (await ReadAsync()).Data.Should().HaveCount(1);
        await _rls.Views.RefreshMaterializedViewAsync(view.ViewId, concurrent: true, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The other way round: a policy added under a materialized view would leave the copy it already
    /// holds readable by everyone. The policy is refused until the view is gone.
    /// </summary>
    [Fact]
    public async Task A_table_a_materialized_view_reads_cannot_be_given_a_policy()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, Row("a", UserA));
        await _rls.SeedAsync(table, Row("b", UserB));
        var a = User(table.ProjectId, UserA);
        var viewName = "rls_mview_" + Guid.NewGuid().ToString("N")[..8];
        var view = await _rls.Views.CreateViewAsync(
            TitlesView(table.ProjectId, viewName, table.LogicalName, materialized: true), TestContext.Current.CancellationToken);

        Task<ViewQueryResult> ReadViewAsync() => As(a, () => _rls.Views.QueryViewAsync(
            new ViewQueryRequest { ProjectId = table.ProjectId, ViewName = viewName }));
        (await ReadViewAsync()).Data.Should().HaveCount(2, "no policy: the copy holds every row, for every caller");

        var create = () => _rls.CreatePolicyAsync(table, "owner = {{user_id}}");

        var refusal = (await create.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("INVALID_OPERATION");
        refusal.Message.Should().Contain($"'{viewName}'").And.Contain($"'{table.LogicalName}'").And.Contain("plain");
        (await _rls.StoredPolicyCountAsync(table)).Should().Be(0);
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(new { Enabled = false, Forced = false });
        (await _rls.NativeStateAsync(table)).Names.Should().BeEmpty();

        // Nor by switching on a policy that is stored but inactive.
        var policyId = await _rls.StoreUnprojectedPolicyAsync(table, "owner = {{user_id}}");
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policyId, new UpdatePolicyRequest { IsActive = false }, TestContext.Current.CancellationToken);
        var activate = () => _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policyId, new UpdatePolicyRequest { IsActive = true });
        (await activate.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_OPERATION");
        (await _rls.Policies.GetPolicyAsync(table.ProjectId, policyId, TestContext.Current.CancellationToken))!
            .IsActive.Should().BeFalse();
        (await _rls.NativeStateAsync(table)).Names.Should().BeEmpty();
        (await ReadViewAsync()).Data.Should().HaveCount(2, "the view is as it was");

        // Once the view is deleted the table can have its policy.
        await _rls.Views.DeleteViewAsync(view.ViewId, TestContext.Current.CancellationToken);
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policyId, new UpdatePolicyRequest { IsActive = true }, TestContext.Current.CancellationToken);
        (await VisibleAsync(table, a)).Should().HaveCount(1);
    }

    /// <summary>
    /// A view and a policy created at the same moment. Each side asks about the other only after
    /// the statement that takes its lock on the table, so whichever PostgreSQL makes wait finds the
    /// other already committed, and is refused.
    /// </summary>
    [Fact]
    public async Task A_materialized_view_and_a_policy_created_at_the_same_moment_do_not_both_succeed()
    {
        var ct = TestContext.Current.CancellationToken;
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, Row("a", UserA));
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The view is still being built, in a transaction of its own, when the policy is created.
        var unfinishedView = "mv_unfinished_" + suffix;
        await using (var building = await _rls.OpenSystemConnectionAsync())
        {
            await using var transaction = await building.BeginTransactionAsync(ct);
            await building.ExecuteAsync(
                $"""CREATE MATERIALIZED VIEW "{unfinishedView}" AS SELECT * FROM "{table.PhysicalName}" WITH NO DATA""",
                transaction: transaction);

            var policy = _rls.CreatePolicyAsync(table, "owner = {{user_id}}");
            await _rls.WaitUntilALockOnIsAwaitedAsync(table);
            await transaction.CommitAsync(ct);

            var created = () => policy;
            (await created.Should().ThrowAsync<SchemaException>()).Which.Message.Should().Contain($"'{unfinishedView}'");
        }

        (await _rls.StoredPolicyCountAsync(table)).Should().Be(0);
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(new { Enabled = false, Forced = false });
        await _rls.ExecuteAsSystemAsync($"""DROP MATERIALIZED VIEW "{unfinishedView}" """);

        // The other way round: the policy is still being written when the view is created.
        var policyId = Guid.NewGuid();
        var viewName = "rls_mview_" + suffix;
        await using (var writing = await _rls.OpenSystemConnectionAsync())
        {
            await using var transaction = await writing.BeginTransactionAsync(ct);
            await writing.ExecuteAsync(
                """
                INSERT INTO morphdb._morph_security_policies
                (id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at)
                VALUES (@Id, @ProjectId, @TableId, 'unfinished', NULL, 0, @Expression, true, 1, NOW(), NOW())
                """,
                new { Id = policyId, table.ProjectId, table.TableId, Expression = "owner = {{user_id}}" },
                transaction);
            await writing.ExecuteAsync(
                $"""ALTER TABLE "{table.PhysicalName}" ENABLE ROW LEVEL SECURITY""", transaction: transaction);

            var view = _rls.Views.CreateViewAsync(TitlesView(table.ProjectId, viewName, table.LogicalName, materialized: true), ct);
            await _rls.WaitUntilALockOnIsAwaitedAsync(table);
            await transaction.CommitAsync(ct);

            var created = () => view;
            (await created.Should().ThrowAsync<SchemaException>()).Which.Message.Should().Contain("'unfinished'");
        }

        (await _rls.Views.GetViewAsync(table.ProjectId, viewName, ct)).Should().BeNull();
        await _rls.Policies.DeletePolicyAsync(table.ProjectId, policyId, ct);
    }

    // ---- bulk jobs: requested by one caller, carried out later by a service that has none -------

    private const string ExportJobs = "_morph_export_jobs";
    private const string ImportJobs = "_morph_import_jobs";

    /// <summary>Requests an export of the titles alone, so each line of the file is one row's title.</summary>
    private Task<BulkExportJob> RequestExportAsync(Documents docs, SecurityContext? requester) =>
        As(requester, () => _rls.Bulk.StartCsvExportAsync(
            docs.ProjectId, docs.Name, new CsvExportOptions { Columns = ["title"] }));

    private async Task<BulkImportJob> RequestImportAsync(
        Documents docs, SecurityContext? requester, params (string Title, string Owner)[] rows)
    {
        var json = "[" + string.Join(",", rows.Select(r => $$"""{"title":"{{r.Title}}","owner":"{{r.Owner}}"}""")) + "]";
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await As(requester, () => _rls.Bulk.StartJsonImportAsync(docs.ProjectId, docs.Name, payload));
    }

    /// <summary>Every title the table holds, read past the policy.</summary>
    private async Task<List<string>> StoredTitlesAsync(Documents docs)
    {
        var rows = await As(docs.Bypass, () => _rls.Data.Query(docs.ProjectId).From(docs.Name).SelectAll().ToListAsync());
        return rows.Select(r => (string)r["title"]!).ToList();
    }

    /// <summary>
    /// The request counts the table's rows as its caller. The job is carried out later by the job
    /// processor, which has no caller — and reads the table as the caller that requested it.
    /// </summary>
    [Fact]
    public async Task An_export_job_reads_the_table_as_the_caller_that_requested_it()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var job = await RequestExportAsync(docs, caller);
            job.TotalRows.Should().Be(visible.Length, $"{who} requests the export");

            var exported = await As(null, () => _rls.RunExportJobAsync(job));
            exported.Should().BeEquivalentTo(visible.Select(docs.TitleOf), $"{who} requested the export");
        }
    }

    /// <summary>
    /// Who carries a job out is not who it runs as: not a caller with rows of its own, and not one
    /// that bypasses every policy. A request that reaches the service's own streaming of a job is in
    /// the same position as the processor.
    /// </summary>
    [Fact]
    public async Task An_export_job_does_not_read_the_table_as_whoever_carries_it_out()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var executors = new (string Who, SecurityContext? Executor)[]
        {
            ("B", docs.B),
            ("bypass", docs.Bypass),
            ("no caller", null),
        };

        foreach (var (who, executor) in executors)
        {
            var job = await RequestExportAsync(docs, docs.A);
            (await As(executor, () => _rls.RunExportJobAsync(job)))
                .Should().BeEquivalentTo(["root-a", "child-a"], $"A requested it and {who} carried it out");

            var streamed = await RequestExportAsync(docs, docs.A);
            using var output = new MemoryStream();
            await As(executor, () => _rls.Bulk.StreamExportAsync(streamed.JobId, output));
            Encoding.UTF8.GetString(output.ToArray()).Should().Contain("root-a").And.Contain("child-a")
                .And.NotContain("root-b").And.NotContain("child-b", $"A requested it and {who} streamed it");
        }
    }

    /// <summary>
    /// A job whose row holds no security context — requested without a caller, or stored by a
    /// version that did not record one — runs as nobody, whoever carries it out. A caller is not
    /// guessed for it.
    /// </summary>
    [Fact]
    public async Task An_export_job_whose_row_holds_no_caller_reads_the_table_as_nobody()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        var requestedByNobody = await RequestExportAsync(docs, null);
        (await _rls.StoredRequesterAsync(ExportJobs, requestedByNobody.JobId)).Should().BeNull();
        (await As(docs.Bypass, () => _rls.RunExportJobAsync(requestedByNobody))).Should().BeEmpty();

        var storedEarlier = await RequestExportAsync(docs, docs.A);
        (await _rls.StoredRequesterAsync(ExportJobs, storedEarlier.JobId)).Should().Contain(UserA);
        await _rls.ForgetRequesterAsync(ExportJobs, storedEarlier.JobId);
        (await As(null, () => _rls.RunExportJobAsync(storedEarlier))).Should().BeEmpty();
        (await As(docs.Bypass, () => _rls.RunExportJobAsync(storedEarlier))).Should().BeEmpty(
            "the row holds no caller, and the one carrying it out is not borrowed");
    }

    /// <summary>
    /// What is stored is the caller PostgreSQL saw: every setting a policy can compare with, and
    /// whether the caller bypasses policies — nothing else of the context.
    /// </summary>
    [Fact]
    public async Task A_job_stores_the_whole_caller_a_policy_can_compare_with()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "s", ["owner"] = UserA, ["department"] = "sales" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "e", ["owner"] = UserA, ["department"] = "engineering" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "x", ["owner"] = UserB, ["department"] = "sales" });
        await _rls.CreatePolicyAsync(
            table,
            "owner = {{user_id}} AND department = {{claims.department}} AND {{role}} = 'analyst' " +
            "AND {{email}} = 'a@example.test' AND {{is_authenticated}} AND {{project_id}} = project_id");

        var requester = User(table.ProjectId, UserA);
        requester.Role = "analyst";
        requester.Email = "a@example.test";
        requester.Claims["department"] = "sales";
        requester.ConfinedToProjectId = table.ProjectId;

        var job = await As(requester, () => _rls.Bulk.StartCsvExportAsync(
            table.ProjectId, table.LogicalName, new CsvExportOptions { Columns = ["title"] }));

        (await As(null, () => _rls.RunExportJobAsync(job))).Should().Equal("s");
        (await _rls.StoredRequesterAsync(ExportJobs, job.JobId)).Should().NotContain("onfined");
    }

    /// <summary>
    /// An import job writes each row as the caller that requested it. B's row stands between two of
    /// A's: the caller is still A for the row after one that was refused, and after the processor
    /// has been handed each row's result.
    /// </summary>
    [Fact]
    public async Task An_import_job_writes_each_row_as_the_caller_that_requested_it()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var job = await RequestImportAsync(docs, docs.A, ("mine-1", UserA), ("theirs", UserB), ("mine-2", UserA));

        var done = await As(null, () => _rls.RunImportJobAsync(job));

        done.Should().BeEquivalentTo(new { SuccessCount = 2L, ErrorCount = 1L });
        var refused = done.ErrorDetails.Should().ContainSingle().Which;
        refused.RowNumber.Should().Be(2);
        refused.Error.Should().Contain("Row-level security");
        (await StoredTitlesAsync(docs)).Should().BeEquivalentTo(
            ["root-a", "child-a", "root-b", "child-b", "mine-1", "mine-2"]);

        // Requested by a caller that bypasses policies, every row is written.
        var unrestricted = await RequestImportAsync(docs, docs.Bypass, ("any-1", UserA), ("any-2", UserB));
        (await As(null, () => _rls.RunImportJobAsync(unrestricted)))
            .Should().BeEquivalentTo(new { SuccessCount = 2L, ErrorCount = 0L });
        (await StoredTitlesAsync(docs)).Should().Contain(["any-1", "any-2"]);
    }

    /// <summary>
    /// An import job whose row holds no security context writes as nobody: under a policy that
    /// compares with the caller every row is refused, and the job says why for each.
    /// </summary>
    [Fact]
    public async Task An_import_job_whose_row_holds_no_caller_has_every_row_refused()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        var requestedByNobody = await RequestImportAsync(docs, null, ("orphan-1", UserA), ("orphan-2", UserA));
        (await _rls.StoredRequesterAsync(ImportJobs, requestedByNobody.JobId)).Should().BeNull();

        var storedEarlier = await RequestImportAsync(docs, docs.A, ("late-1", UserA), ("late-2", UserA));
        await _rls.ForgetRequesterAsync(ImportJobs, storedEarlier.JobId);

        foreach (var job in new[] { requestedByNobody, storedEarlier })
        {
            // Carried out by a caller that could write the rows itself: it is not borrowed.
            var done = await As(docs.Bypass, () => _rls.RunImportJobAsync(job));

            done.Should().BeEquivalentTo(new { SuccessCount = 0L, ErrorCount = 2L });
            done.ErrorDetails.Should().HaveCount(2).And.OnlyContain(e => e.Error.Contains("Row-level security"));
        }

        (await StoredTitlesAsync(docs)).Should().HaveCount(4, "nothing was imported");
    }

    /// <summary>
    /// The processor carries out one job after another in one flow. Each runs as its own requester,
    /// the next does not inherit the last one's, and the flow is left with the caller it had — which
    /// the service puts back itself rather than leaving to how an asynchronous call happens to flow.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Jobs_carried_out_back_to_back_each_run_as_their_own_requester_and_leave_the_flow_as_it_was(
        bool carriedOutByACaller)
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var executor = carriedOutByACaller ? User(docs.ProjectId, "the-executor") : null;

        var exportForA = await RequestExportAsync(docs, docs.A);
        var exportForB = await RequestExportAsync(docs, docs.B);
        var importForA = await RequestImportAsync(docs, docs.A, ("mine", UserA), ("theirs", UserB));
        var exportForNobody = await RequestExportAsync(docs, null);

        await As(executor, async () =>
        {
            (await _rls.RunExportJobAsync(exportForA)).Should().BeEquivalentTo(["root-a", "child-a"]);
            ReferenceEquals(Ambient, executor).Should().BeTrue("the flow has the caller it had before the job");

            (await _rls.RunExportJobAsync(exportForB)).Should().BeEquivalentTo(["root-b", "child-b"]);
            ReferenceEquals(Ambient, executor).Should().BeTrue();

            (await _rls.RunImportJobAsync(importForA)).Should().BeEquivalentTo(new { SuccessCount = 1L, ErrorCount = 1L });
            ReferenceEquals(Ambient, executor).Should().BeTrue();

            (await _rls.RunExportJobAsync(exportForNobody)).Should().BeEmpty("it inherits neither A nor the executor");
            ReferenceEquals(Ambient, executor).Should().BeTrue();
        });

        // In order: each export once, and the import once for each of its two rows. Every context
        // the service set is followed by its putting back the one that was there.
        var changes = _rls.BulkCallers.Changes;
        changes.Where((_, i) => i % 2 == 0).Select(c => c?.UserId).Should().Equal(UserA, UserB, UserA, UserA, null);
        changes.Where((_, i) => i % 2 == 1).Should().HaveCount(5).And.OnlyContain(c => ReferenceEquals(c, executor));
    }

    /// <summary>
    /// A lookup column reads another table through a join. The policy belongs to that other table,
    /// so the join finds no row where the caller may not read one.
    /// </summary>
    [Fact]
    public async Task A_lookup_into_the_table_yields_nothing_for_a_hidden_target()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var notes = await _rls.Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = docs.ProjectId,
            LogicalName = "rls_notes_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "label", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "document_id", DataType = MorphDataType.Uuid, IsNullable = true },
                new CreateColumnRequest
                {
                    LogicalName = "document_title",
                    DataType = MorphDataType.Text,
                    IsNullable = true,
                    LookupConfig = new LookupColumnConfig
                    {
                        RelationColumn = "document_id",
                        TargetTable = docs.Name,
                        TargetColumn = "title",
                        MatchColumn = SystemColumns.Id
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        await _rls.SeedAsync(notes, new Dictionary<string, object?> { ["label"] = "about-a", ["document_id"] = docs.RootA });
        await _rls.SeedAsync(notes, new Dictionary<string, object?> { ["label"] = "about-b", ["document_id"] = docs.RootB });

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var rows = await As(caller, () => _rls.Data.Query(docs.ProjectId).From(notes.LogicalName).SelectAll().ToListAsync());
            var titles = rows.ToDictionary(r => (string)r["label"]!, r => (string?)r["document_title"]);

            // The notes table has no policy of its own: every caller reads both notes.
            titles.Keys.Should().BeEquivalentTo(["about-a", "about-b"], who);
            titles["about-a"].Should().Be(visible.Contains(docs.RootA) ? "root-a" : null, who);
            titles["about-b"].Should().Be(visible.Contains(docs.RootB) ? "root-b" : null, who);
        }
    }

    /// <summary>A rollup aggregates another table's rows; it aggregates the ones its reader may read.</summary>
    [Fact]
    public async Task A_rollup_over_the_table_counts_only_the_rows_the_caller_may_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var folders = await _rls.Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = docs.ProjectId,
            LogicalName = "rls_folders_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "label", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest
                {
                    LogicalName = "document_count",
                    DataType = MorphDataType.Integer,
                    IsNullable = true,
                    RollupConfig = new RollupColumnConfig
                    {
                        Relation = "folder_documents",
                        TargetTable = docs.Name,
                        ForeignKeyColumn = "folder_id",
                        SourceColumn = "*",
                        Aggregation = RollupAggregation.Count
                    }
                }
            ]
        }, TestContext.Current.CancellationToken);
        await _rls.SeedAsync(folders, new Dictionary<string, object?> { [SystemColumns.Id] = docs.Folder, ["label"] = "all" });

        foreach (var (who, caller, visible) in docs.Callers)
        {
            var folder = await As(caller, () => _rls.Data.GetByIdAsync(docs.ProjectId, folders.LogicalName, docs.Folder));
            Convert.ToInt64(folder!["document_count"], CultureInfo.InvariantCulture).Should().Be(visible.Length, who);
        }
    }

    // ---- writes -------------------------------------------------------------------------------

    [Fact]
    public async Task Updating_a_hidden_row_by_id_finds_no_row_and_changes_nothing()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var change = new Dictionary<string, object?> { ["title"] = "taken over" };

        var act = () => As(docs.A, () => _rls.Data.UpdateAsync(docs.ProjectId, docs.Name, docs.RootB, change));
        await act.Should().ThrowAsync<NotFoundException>("a row the caller cannot read is not there to be updated");

        var noCaller = () => As(null, () => _rls.Data.UpdateAsync(docs.ProjectId, docs.Name, docs.RootB, change));
        await noCaller.Should().ThrowAsync<NotFoundException>();

        (await _rls.StoredAsync(docs.Table, docs.RootB))!["title"].Should().Be("root-b");

        // The owner, and a caller that bypasses the policy, still can.
        await As(docs.B, () => _rls.Data.UpdateAsync(docs.ProjectId, docs.Name, docs.RootB, change));
        (await _rls.StoredAsync(docs.Table, docs.RootB))!["title"].Should().Be("taken over");
        await As(docs.Bypass, () => _rls.Data.UpdateAsync(
            docs.ProjectId, docs.Name, docs.RootB, new Dictionary<string, object?> { ["title"] = "restored" }));
        (await _rls.StoredAsync(docs.Table, docs.RootB))!["title"].Should().Be("restored");
    }

    [Fact]
    public async Task Deleting_a_hidden_row_by_id_finds_no_row_and_changes_nothing()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        (await As(docs.A, () => _rls.Data.DeleteAsync(docs.ProjectId, docs.Name, docs.RootB)))
            .Should().BeFalse("a row the caller cannot read is not there to be deleted");
        (await As(null, () => _rls.Data.DeleteAsync(docs.ProjectId, docs.Name, docs.RootB))).Should().BeFalse();
        (await _rls.StoredAsync(docs.Table, docs.RootB)).Should().NotBeNull();

        (await As(docs.B, () => _rls.Data.DeleteAsync(docs.ProjectId, docs.Name, docs.RootB))).Should().BeTrue();
        (await _rls.StoredAsync(docs.Table, docs.RootB)).Should().BeNull();
    }

    [Fact]
    public async Task A_filtered_update_or_delete_reaches_only_the_rows_the_caller_may_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        // No filter narrows these: the policy alone decides which rows they touch.
        var everything = _rls.Data.Query(docs.ProjectId).From(docs.Name).Where("title", FilterOperator.IsNotNull, null);
        var updated = await As(docs.A, () => _rls.Data.UpdateBatchAsync(
            docs.ProjectId, docs.Name, new Dictionary<string, object?> { ["scratch"] = "touched" }, everything));
        updated.Should().Be(2);
        (await _rls.StoredAsync(docs.Table, docs.RootB))!["scratch"].Should().BeNull();

        var deleted = await As(docs.A, () => _rls.Data.DeleteBatchAsync(docs.ProjectId, docs.Name, everything));
        deleted.Should().Be(2);
        (await _rls.StoredAsync(docs.Table, docs.RootB)).Should().NotBeNull();
        (await _rls.StoredAsync(docs.Table, docs.ChildBUnderA)).Should().NotBeNull();
    }

    [Fact]
    public async Task A_transaction_cannot_update_a_hidden_row()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        var result = await As(docs.A, () => _rls.Transactions.ExecuteAsync(docs.ProjectId, new TransactionRequest
        {
            Operations =
            [
                new TransactionOperation
                {
                    Method = "UPDATE",
                    Table = docs.Name,
                    Id = docs.RootB,
                    Data = new Dictionary<string, object?> { ["title"] = "taken over" }
                }
            ]
        }));

        result.Success.Should().BeFalse();
        (await _rls.StoredAsync(docs.Table, docs.RootB))!["title"].Should().Be("root-b");
    }

    /// <summary>
    /// A write hands back the row it wrote, and PostgreSQL will not hand a caller a row the policy
    /// hides from it — so a policy on reads also refuses a write whose result its caller could not
    /// read. A row the caller may read is written as before.
    /// </summary>
    [Fact]
    public async Task A_write_is_refused_when_its_caller_could_not_read_the_row_it_leaves()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();

        // Inserting a row of one's own is untouched by the policy.
        var own = await As(docs.A, () => _rls.Data.InsertAsync(docs.ProjectId, docs.Name, Row("mine", UserA)));
        (await _rls.StoredAsync(docs.Table, IdOf(own)))!["title"].Should().Be("mine");

        // Inserting a row for someone else, or as nobody, is not.
        var forB = () => As(docs.A, () => _rls.Data.InsertAsync(docs.ProjectId, docs.Name, Row("for-b", UserB)));
        (await forB.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain(docs.Name);

        var asNobody = () => As(null, () => _rls.Data.InsertAsync(docs.ProjectId, docs.Name, Row("orphan", UserA)));
        await asNobody.Should().ThrowAsync<ForbiddenException>();

        // Giving one's own row away.
        var giveAway = () => As(docs.A, () => _rls.Data.UpdateAsync(
            docs.ProjectId, docs.Name, docs.RootA, new Dictionary<string, object?> { ["owner"] = UserB }));
        await giveAway.Should().ThrowAsync<ForbiddenException>();
        (await _rls.StoredAsync(docs.Table, docs.RootA))!["owner"].Should().Be(UserA);

        var giveAwayInBulk = () => As(docs.A, () => _rls.Data.UpdateBatchAsync(
            docs.ProjectId,
            docs.Name,
            new Dictionary<string, object?> { ["owner"] = UserB },
            _rls.Data.Query(docs.ProjectId).From(docs.Name).Where("title", FilterOperator.Equals, "root-a")));
        await giveAwayInBulk.Should().ThrowAsync<ForbiddenException>();

        // An upsert that lands on a row its caller cannot read.
        var upsert = () => As(docs.A, () => _rls.Data.UpsertAsync(
            docs.ProjectId,
            docs.Name,
            new Dictionary<string, object?> { [SystemColumns.Id] = docs.RootB, ["title"] = "taken over", ["owner"] = UserA },
            [SystemColumns.Id]));
        await upsert.Should().ThrowAsync<ForbiddenException>();
        (await _rls.StoredAsync(docs.Table, docs.RootB))!["title"].Should().Be("root-b");

        // A caller that bypasses the policy writes anything.
        var bypassed = await As(docs.Bypass, () => _rls.Data.InsertAsync(docs.ProjectId, docs.Name, Row("for-b", UserB)));
        (await _rls.StoredAsync(docs.Table, IdOf(bypassed)))!["owner"].Should().Be(UserB);
    }

    /// <summary>
    /// A relation is checked by reading the row it points at, as the writer. A row the writer may
    /// not read is, to that check, a row that does not exist.
    /// </summary>
    [Fact]
    public async Task A_relation_cannot_point_at_a_row_its_writer_may_not_read()
    {
        var docs = await CreateDocumentsWithOwnerPolicyAsync();
        var remarks = await _rls.Schema.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = docs.ProjectId,
            LogicalName = "rls_remarks_" + Guid.NewGuid().ToString("N")[..8],
            Columns =
            [
                new CreateColumnRequest { LogicalName = "body", DataType = MorphDataType.Text, IsNullable = false },
                new CreateColumnRequest { LogicalName = "document_id", DataType = MorphDataType.Uuid, IsNullable = true },
            ]
        }, TestContext.Current.CancellationToken);
        await _rls.Schema.CreateRelationAsync(new CreateRelationRequest
        {
            ProjectId = docs.ProjectId,
            LogicalName = "remark_document_" + Guid.NewGuid().ToString("N")[..8],
            SourceTableId = remarks.TableId,
            SourceColumnId = remarks.Columns.Single(c => c.LogicalName == "document_id").ColumnId,
            TargetTableId = docs.Table.TableId,
            TargetColumnId = docs.Table.Columns.Single(c => c.LogicalName == SystemColumns.Id).ColumnId,
            RelationType = RelationType.OneToMany
        }, TestContext.Current.CancellationToken);

        Task<IDictionary<string, object?>> RemarkOnAsync(SecurityContext caller, Guid document) => As(caller, () =>
            _rls.Data.InsertAsync(docs.ProjectId, remarks.LogicalName, new Dictionary<string, object?>
            {
                ["body"] = "noted",
                ["document_id"] = document
            }));

        await RemarkOnAsync(docs.A, docs.RootA);

        var onHidden = () => RemarkOnAsync(docs.A, docs.RootB);
        (await onHidden.Should().ThrowAsync<ValidationException>()).Which.Message.Should().Contain("does not exist");

        await RemarkOnAsync(docs.Bypass, docs.RootB);
    }

    // ---- what a policy may compare with --------------------------------------------------------

    /// <summary>
    /// A placeholder is read from the session as text. Compared with a uuid column it has to be a
    /// uuid, and PostgreSQL is asked which — the way an untyped literal was typed by its position.
    /// </summary>
    [Fact]
    public async Task A_placeholder_compared_with_a_uuid_column_is_read_as_a_uuid()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var rowA = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "a", ["owner_id"] = a });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "b", ["owner_id"] = b });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "unowned", ["owner_id"] = null });

        await _rls.CreatePolicyAsync(table, "owner_id = {{user_id}}");

        (await VisibleAsync(table, User(table.ProjectId, a.ToString()))).Should().Equal(rowA);
        (await VisibleAsync(table, null)).Should().BeEmpty("no caller is NULL, and nothing equals NULL");
        (await VisibleAsync(table, SecurityContext.Service(table.ProjectId))).Should().HaveCount(3);

        // A caller whose id is not a uuid cannot be compared with one. A caller that bypasses the
        // policy is never compared at all, whatever its id is.
        var notAUuid = SecurityContext.Service(table.ProjectId);
        notAUuid.UserId = "not-a-uuid";
        (await VisibleAsync(table, notAUuid)).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_policy_can_compare_with_a_claim()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        var sales = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "s", ["department"] = "sales" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "e", ["department"] = "engineering" });

        await _rls.CreatePolicyAsync(table, "department = {{claims.department}}");

        var inSales = User(table.ProjectId, UserA);
        inSales.Claims["department"] = "sales";
        (await VisibleAsync(table, inSales)).Should().Equal(sales);

        var quoted = User(table.ProjectId, UserA);
        quoted.Claims["department"] = "sales' OR '1'='1";
        (await VisibleAsync(table, quoted)).Should().BeEmpty("a claim is a value, whatever it contains");

        (await VisibleAsync(table, User(table.ProjectId, UserA))).Should().BeEmpty("an absent claim is NULL");
    }

    [Fact]
    public async Task A_policy_can_ask_whether_the_caller_is_authenticated()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "one" });

        await _rls.CreatePolicyAsync(table, "{{is_authenticated}}");

        (await VisibleAsync(table, User(table.ProjectId, UserA))).Should().HaveCount(1);
        (await VisibleAsync(table, SecurityContext.Anonymous(table.ProjectId))).Should().BeEmpty();
        (await VisibleAsync(table, null)).Should().BeEmpty();
    }

    /// <summary>
    /// Where nothing fixes a placeholder's type it is text, as the literal it used to become was.
    /// </summary>
    [Fact]
    public async Task A_placeholder_nothing_types_is_text()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "one" });

        await _rls.CreatePolicyAsync(table, "{{user_id}} IS NOT NULL AND {{role}} = 'analyst' AND {{project_id}} = project_id");

        var analyst = User(table.ProjectId, UserA);
        analyst.Role = "analyst";
        (await VisibleAsync(table, analyst)).Should().HaveCount(1);

        (await VisibleAsync(table, User(table.ProjectId, UserA))).Should().BeEmpty("the caller has no role");

        var elsewhere = User(Guid.NewGuid(), UserA);
        elsewhere.Role = "analyst";
        (await VisibleAsync(table, elsewhere, table.ProjectId)).Should().BeEmpty("the caller is in another project");
    }

    [Fact]
    public async Task Several_policies_on_a_table_must_all_admit_a_row()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        var both = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "x", ["owner"] = UserA, ["department"] = "sales" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "y", ["owner"] = UserA, ["department"] = "engineering" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "z", ["owner"] = UserB, ["department"] = "sales" });

        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "owner_only");
        await _rls.CreatePolicyAsync(table, "department = 'sales'", "sales_only");

        (await VisibleAsync(table, User(table.ProjectId, UserA))).Should().Equal(both);
    }

    private async Task<List<Guid>> VisibleAsync(TableMetadata table, SecurityContext? caller, Guid? projectId = null)
    {
        var rows = await As(caller, () => _rls.Data.Query(projectId ?? table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync());
        return rows.Select(IdOf).ToList();
    }
}
