using Microsoft.Extensions.Logging.Abstractions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Schema;
using MorphDB.Npgsql.Security;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration;

/// <summary>
/// A policy's type is the command it restricts. <c>Insert</c> decides which rows a caller may
/// create, <c>Update</c> which rows it may change and what it may change them into, <c>Delete</c>
/// which rows it may remove, and <c>All</c> is all of those and reading besides. Each is enforced by
/// PostgreSQL, so every write path meets it: a write by id, a filtered write, an upsert, a
/// transaction, a finalize.
/// </summary>
[Collection("PostgreSQL")]
public class NativeWritePolicyTests
{
    private const string OwnRowsOnly = "owner = {{user_id}}";

    private readonly PostgresFixture _fixture;
    private readonly RowLevelSecurityHarness _rls;

    public NativeWritePolicyTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _rls = new RowLevelSecurityHarness(fixture);
    }

    /// <summary>
    /// One row for A and one for B, under one policy of each type asked for — every one of them
    /// <c>owner = {{user_id}}</c>. A test changes the rows and then puts them back.
    /// </summary>
    private sealed record Scene(TableMetadata Table, Guid RowA, Guid RowB)
    {
        public Guid ProjectId => Table.ProjectId;
        public string Name => Table.LogicalName;
        public SecurityContext A => User(ProjectId, UserA);
        public SecurityContext Bypass => SecurityContext.Service(ProjectId);

        public Guid RowOf(string owner) => owner == UserA ? RowA : RowB;
    }

    private async Task<Scene> CreateSceneAsync(params PolicyType[] types)
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid(), "rls_write");
        var scene = new Scene(table, Guid.NewGuid(), Guid.NewGuid());
        await ResetAsync(scene);

        foreach (var type in types)
        {
            await _rls.CreatePolicyAsync(table, OwnRowsOnly, $"own_rows_{type}", type);
        }

        return scene;
    }

    /// <summary>Leaves the table holding A's row and B's row as they were first written.</summary>
    private async Task ResetAsync(Scene scene)
    {
        await As(scene.Bypass, async () =>
        {
            await _rls.Data.DeleteBatchAsync(scene.ProjectId, scene.Name, Everything(scene));
            await _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("a", UserA, scene.RowA));
            await _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("b", UserB, scene.RowB));
        });
    }

    private IMorphQuery Everything(Scene scene) =>
        _rls.Data.Query(scene.ProjectId).From(scene.Name).Where("title", FilterOperator.IsNotNull, null);

    private IMorphQuery Titled(Scene scene, string title) =>
        _rls.Data.Query(scene.ProjectId).From(scene.Name).Where("title", FilterOperator.Equals, title);

    private static Dictionary<string, object?> Row(string title, string owner, Guid? id = null)
    {
        var row = new Dictionary<string, object?> { ["title"] = title, ["owner"] = owner };
        if (id is not null)
        {
            row[SystemColumns.Id] = id;
        }

        return row;
    }

    /// <summary>What the table holds, read past every policy: <c>title:owner:scratch</c> per row, by title.</summary>
    private async Task<string[]> StoredAsync(Scene scene)
    {
        var rows = await As(scene.Bypass, () => _rls.Data.Query(scene.ProjectId).From(scene.Name).SelectAll().ToListAsync());
        return [.. rows.Select(r => $"{r["title"]}:{r["owner"]}:{r["scratch"]}").Order(StringComparer.Ordinal)];
    }

    private static readonly string[] AsSeeded = [$"a:{UserA}:", $"b:{UserB}:"];

    /// <summary>What a set of policy types restricts, for a caller that does not bypass them.</summary>
    private sealed record Restricted(bool Reads, bool Inserts, bool Updates, bool Deletes)
    {
        public static Restricted By(PolicyType[] types) => new(
            Reads: types.Any(t => t is PolicyType.Select or PolicyType.All),
            Inserts: types.Any(t => t is PolicyType.Insert or PolicyType.All),
            Updates: types.Any(t => t is PolicyType.Update or PolicyType.All),
            Deletes: types.Any(t => t is PolicyType.Delete or PolicyType.All));
    }

    /// <param name="Owns">The owner whose rows the policy admits to this caller; <c>null</c> for none.</param>
    private sealed record Caller(string Who, SecurityContext? Context, string? Owns, bool Bypasses)
    {
        /// <summary>Whether a restriction of the table lets this caller through for a row of that owner.</summary>
        public bool Passes(bool restricted, string owner) => !restricted || Bypasses || Owns == owner;
    }

    private static Caller[] CallersOf(Scene scene) =>
    [
        new("A", scene.A, UserA, Bypasses: false),
        new("no caller", null, null, Bypasses: false),
        new("bypass", scene.Bypass, null, Bypasses: true),
    ];

    private static string Other(string owner) => owner == UserA ? UserB : UserA;

    private static string TitleOf(string owner) => owner == UserA ? "a" : "b";

    // ---- the matrix: every write path, under every type, as every kind of caller ---------------

    /// <summary>
    /// Every write MorphDB makes hands back the row it wrote, so a policy on reads also decides what
    /// a write may leave; that is why <c>Reads</c> appears beside the write's own restriction below.
    /// What is asserted for each write is the answer its caller gets and, read past the policies,
    /// what the table then holds.
    /// </summary>
    [Theory]
    [InlineData(PolicyType.Select)]
    [InlineData(PolicyType.Insert)]
    [InlineData(PolicyType.Update)]
    [InlineData(PolicyType.Delete)]
    [InlineData(PolicyType.All)]
    [InlineData(PolicyType.Select, PolicyType.Update)]
    [InlineData(PolicyType.Insert, PolicyType.Delete)]
    public async Task Each_policy_type_restricts_its_own_command_on_every_write_path(params PolicyType[] types)
    {
        var scene = await CreateSceneAsync(types);
        var restricted = Restricted.By(types);

        foreach (var caller in CallersOf(scene))
        {
            foreach (var owner in new[] { UserA, UserB })
            {
                var row = scene.RowOf(owner);
                var title = TitleOf(owner);
                var other = Other(owner);
                var why = $"{caller.Who}, a row of {owner}, under {string.Join("+", types)}";

                // A row is there for an update or a delete when the caller may read it and the
                // policy for that command admits it. A row an update would leave has to pass both
                // as well; so does a row an insert creates.
                var mayInsert = caller.Passes(restricted.Inserts, owner) && caller.Passes(restricted.Reads, owner);
                var findsToUpdate = caller.Passes(restricted.Updates, owner) && caller.Passes(restricted.Reads, owner);
                var mayLeaveWithOther = caller.Passes(restricted.Updates, other) && caller.Passes(restricted.Reads, other);
                var findsToDelete = caller.Passes(restricted.Deletes, owner) && caller.Passes(restricted.Reads, owner);

                // Insert.
                var insert = () => As(caller.Context, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("new", owner)));
                if (mayInsert)
                {
                    await insert.Should().NotThrowAsync(why);
                    (await StoredAsync(scene)).Should().Equal([.. AsSeeded, $"new:{owner}:"], why);
                }
                else
                {
                    await insert.Should().ThrowAsync<ForbiddenException>(why);
                    (await StoredAsync(scene)).Should().Equal(AsSeeded, why);
                }

                await ResetAsync(scene);

                // Update by id.
                var update = () => As(caller.Context, () => _rls.Data.UpdateAsync(
                    scene.ProjectId, scene.Name, row, new Dictionary<string, object?> { ["scratch"] = "changed" }));
                if (findsToUpdate)
                {
                    await update.Should().NotThrowAsync(why);
                    (await StoredAsync(scene)).Should().Contain($"{title}:{owner}:changed", why);
                }
                else
                {
                    await update.Should().ThrowAsync<NotFoundException>(why);
                    (await StoredAsync(scene)).Should().Equal(AsSeeded, why);
                }

                await ResetAsync(scene);

                // Update by id into a row of the other owner, and the same through a filter.
                var giveAway = () => As(caller.Context, () => _rls.Data.UpdateAsync(
                    scene.ProjectId, scene.Name, row, new Dictionary<string, object?> { ["owner"] = other }));
                var giveAwayThroughAFilter = () => As(caller.Context, () => _rls.Data.UpdateBatchAsync(
                    scene.ProjectId, scene.Name, new Dictionary<string, object?> { ["owner"] = other }, Titled(scene, title)));
                if (!findsToUpdate)
                {
                    await giveAway.Should().ThrowAsync<NotFoundException>(why);
                    (await giveAwayThroughAFilter()).Should().Be(0, why);
                    (await StoredAsync(scene)).Should().Equal(AsSeeded, why);
                }
                else if (!mayLeaveWithOther)
                {
                    await giveAway.Should().ThrowAsync<ForbiddenException>(why);
                    await giveAwayThroughAFilter.Should().ThrowAsync<ForbiddenException>(why);
                    (await StoredAsync(scene)).Should().Equal(AsSeeded, why);
                }
                else
                {
                    await giveAway.Should().NotThrowAsync(why);
                    (await StoredAsync(scene)).Should().Contain($"{title}:{other}:", why);
                    await ResetAsync(scene);
                    (await giveAwayThroughAFilter()).Should().Be(1, why);
                    (await StoredAsync(scene)).Should().Contain($"{title}:{other}:", why);
                }

                await ResetAsync(scene);

                // Delete by id.
                (await As(caller.Context, () => _rls.Data.DeleteAsync(scene.ProjectId, scene.Name, row)))
                    .Should().Be(findsToDelete, why);
                (await StoredAsync(scene)).Should().HaveCount(findsToDelete ? 1 : 2, why)
                    .And.Contain($"{TitleOf(other)}:{other}:", why);

                await ResetAsync(scene);
            }

            // Through a filter that matches every row, the policies alone decide which are reached.
            var reason = $"{caller.Who}, under {string.Join("+", types)}";
            var updatable = new[] { UserA, UserB }
                .Where(o => caller.Passes(restricted.Updates, o) && caller.Passes(restricted.Reads, o)).ToList();
            var deletable = new[] { UserA, UserB }
                .Where(o => caller.Passes(restricted.Deletes, o) && caller.Passes(restricted.Reads, o)).ToList();

            var updated = await As(caller.Context, () => _rls.Data.UpdateBatchAsync(
                scene.ProjectId, scene.Name, new Dictionary<string, object?> { ["scratch"] = "touched" }, Everything(scene)));
            updated.Should().Be(updatable.Count, reason);
            (await StoredAsync(scene)).Should().Equal(
                new[] { UserA, UserB }.Select(o => $"{TitleOf(o)}:{o}:{(updatable.Contains(o) ? "touched" : "")}"), reason);

            await ResetAsync(scene);

            var deleted = await As(caller.Context, () => _rls.Data.DeleteBatchAsync(scene.ProjectId, scene.Name, Everything(scene)));
            deleted.Should().Be(deletable.Count, reason);
            (await StoredAsync(scene)).Should().Equal(
                new[] { UserA, UserB }.Where(o => !deletable.Contains(o)).Select(o => $"{TitleOf(o)}:{o}:"), reason);

            await ResetAsync(scene);
        }
    }

    // ---- the same, spelled out for the combinations a schema is most likely to use -------------

    /// <summary>
    /// With a policy for inserts and none for reads, a caller creates only rows the policy admits —
    /// and goes on reading, changing and removing every row, because nothing restricts those.
    /// </summary>
    [Fact]
    public async Task With_an_insert_policy_alone_a_caller_inserts_only_its_own_rows_and_still_reads_every_row()
    {
        var scene = await CreateSceneAsync(PolicyType.Insert);

        await As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("mine", UserA)));

        var forB = () => As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("theirs", UserB)));
        (await forB.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Contain(scene.Name);
        var asNobody = () => As(null, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("orphan", UserA)));
        await asNobody.Should().ThrowAsync<ForbiddenException>();

        // Several rows are one transaction: the row that is refused takes the others with it.
        var several = () => As(scene.A, () => _rls.Data.InsertBatchAsync(
            scene.ProjectId, scene.Name, [Row("mine-2", UserA), Row("theirs-2", UserB)]));
        await several.Should().ThrowAsync<ForbiddenException>();

        (await StoredAsync(scene)).Should().Equal($"a:{UserA}:", $"b:{UserB}:", $"mine:{UserA}:");

        foreach (var caller in new[] { scene.A, null })
        {
            (await As(caller, () => _rls.Data.Query(scene.ProjectId).From(scene.Name).CountAsync())).Should().Be(3);
            (await As(caller, () => _rls.Data.GetByIdAsync(scene.ProjectId, scene.Name, scene.RowB))).Should().NotBeNull();
        }

        // Nothing but the insert is restricted: B's row can be changed, taken and removed.
        await As(scene.A, () => _rls.Data.UpdateAsync(
            scene.ProjectId, scene.Name, scene.RowB, new Dictionary<string, object?> { ["owner"] = UserA }));
        (await As(scene.A, () => _rls.Data.DeleteAsync(scene.ProjectId, scene.Name, scene.RowB))).Should().BeTrue();
    }

    /// <summary>
    /// A policy for updates leaves a row it does not admit readable — and, to its caller, not there
    /// to be updated: the answer is the one a row that does not exist gets, on every path that
    /// updates a row by id.
    /// </summary>
    [Fact]
    public async Task A_row_an_update_policy_does_not_admit_is_readable_and_not_there_to_be_updated()
    {
        var scene = await CreateSceneAsync(PolicyType.Update);
        var change = new Dictionary<string, object?> { ["scratch"] = "changed" };

        (await As(scene.A, () => _rls.Data.GetByIdAsync(scene.ProjectId, scene.Name, scene.RowB))).Should().NotBeNull();

        var update = () => As(scene.A, () => _rls.Data.UpdateAsync(scene.ProjectId, scene.Name, scene.RowB, change));
        await update.Should().ThrowAsync<NotFoundException>();

        var inATransaction = await As(scene.A, () => _rls.Transactions.ExecuteAsync(scene.ProjectId, new TransactionRequest
        {
            Operations = [new TransactionOperation { Method = "UPDATE", Table = scene.Name, Id = scene.RowB, Data = change }]
        }));
        inATransaction.Success.Should().BeFalse();

        // Finalizing a draft reads the row, which this caller may, and then updates its state.
        await _rls.ExecutePastPoliciesAsync($"""UPDATE "{scene.Table.PhysicalName}" SET "_row_state" = 'draft' """);
        var finalized = await As(scene.A, () => _rls.Transactions.FinalizeAsync(scene.ProjectId, scene.Name, scene.RowB));
        finalized.Success.Should().BeFalse();
        finalized.Errors.Should().ContainSingle(e => e.Error == "not_found");
        (await As(scene.A, () => _rls.Transactions.FinalizeAsync(scene.ProjectId, scene.Name, scene.RowA))).Success.Should().BeTrue();

        (await StoredAsync(scene)).Should().Equal(AsSeeded);

        // Giving one's own row away is refused rather than not found: the row is there, and it is
        // what the update would leave that the policy does not admit.
        var giveAway = () => As(scene.A, () => _rls.Data.UpdateAsync(
            scene.ProjectId, scene.Name, scene.RowA, new Dictionary<string, object?> { ["owner"] = UserB }));
        await giveAway.Should().ThrowAsync<ForbiddenException>();

        // Inserting and deleting are not updates.
        await As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("theirs", UserB)));
        (await As(scene.A, () => _rls.Data.DeleteAsync(scene.ProjectId, scene.Name, scene.RowB))).Should().BeTrue();
    }

    /// <summary>
    /// Policies of different types on one table each restrict their own command, and all of them
    /// hold at once: here a caller reads only its own rows and updates only its own rows.
    /// </summary>
    [Fact]
    public async Task A_select_policy_and_an_update_policy_hold_together()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid(), "rls_write");
        var mine = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "mine", ["owner"] = UserA, ["department"] = "open" });
        var locked = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "locked", ["owner"] = UserA, ["department"] = "locked" });
        var theirs = await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "theirs", ["owner"] = UserB, ["department"] = "open" });
        await _rls.CreatePolicyAsync(table, OwnRowsOnly, "reads_own", PolicyType.Select);
        await _rls.CreatePolicyAsync(table, "department <> 'locked'", "updates_unlocked", PolicyType.Update);
        var a = User(table.ProjectId, UserA);
        var change = new Dictionary<string, object?> { ["scratch"] = "changed" };

        (await As(a, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).CountAsync())).Should().Be(2);

        await As(a, () => _rls.Data.UpdateAsync(table.ProjectId, table.LogicalName, mine, change));

        // Readable, and kept from the update by the policy for updates.
        var updateLocked = () => As(a, () => _rls.Data.UpdateAsync(table.ProjectId, table.LogicalName, locked, change));
        await updateLocked.Should().ThrowAsync<NotFoundException>();

        // Admitted by the policy for updates, and hidden by the policy for reads.
        var updateTheirs = () => As(a, () => _rls.Data.UpdateAsync(table.ProjectId, table.LogicalName, theirs, change));
        await updateTheirs.Should().ThrowAsync<NotFoundException>();

        // Each policy refuses the row an update would leave: one it could not read, one it could not update.
        var giveAway = () => As(a, () => _rls.Data.UpdateAsync(
            table.ProjectId, table.LogicalName, mine, new Dictionary<string, object?> { ["owner"] = UserB }));
        await giveAway.Should().ThrowAsync<ForbiddenException>();
        var lockIt = () => As(a, () => _rls.Data.UpdateAsync(
            table.ProjectId, table.LogicalName, mine, new Dictionary<string, object?> { ["department"] = "locked" }));
        await lockIt.Should().ThrowAsync<ForbiddenException>();

        (await _rls.StoredAsync(table, mine))!["scratch"].Should().Be("changed");
        (await _rls.StoredAsync(table, mine))!["department"].Should().Be("open");
        (await _rls.StoredAsync(table, locked))!["scratch"].Should().BeNull();
        (await _rls.StoredAsync(table, theirs))!["scratch"].Should().BeNull();
    }

    /// <summary>
    /// An upsert is an insert that may turn into an update, and PostgreSQL asks the policies for
    /// both — in its own way. The row the caller proposes is checked as an insert even when it ends
    /// as an update; and a row the upsert lands on that a policy for updates does not admit refuses
    /// the statement, where an update by id would simply not have found it.
    /// </summary>
    [Fact]
    public async Task An_upsert_answers_to_the_insert_policy_for_the_row_it_proposes_and_to_the_update_policy_for_the_row_it_lands_on()
    {
        Task<UpsertResult> UpsertAsync(Scene scene, Guid id, string owner) => As(scene.A, () =>
            _rls.Data.UpsertAsync(
                scene.ProjectId,
                scene.Name,
                new Dictionary<string, object?> { [SystemColumns.Id] = id, ["title"] = "upserted", ["owner"] = owner },
                [SystemColumns.Id]));

        // Insert policy: the proposed row is what counts, whichever row it lands on.
        var inserts = await CreateSceneAsync(PolicyType.Insert);
        await UpsertAsync(inserts, inserts.RowA, UserA);
        var proposedForB = () => UpsertAsync(inserts, inserts.RowB, UserB);
        await proposedForB.Should().ThrowAsync<ForbiddenException>("the proposed row is B's, though the upsert would only update");
        (await StoredAsync(inserts)).Should().Equal($"b:{UserB}:", $"upserted:{UserA}:");

        // Nothing restricts updates there, so a row proposed as A's replaces B's.
        await UpsertAsync(inserts, inserts.RowB, UserA);
        (await StoredAsync(inserts)).Should().Equal($"upserted:{UserA}:", $"upserted:{UserA}:");

        // Update policy: landing on a row it does not admit is a refusal, not a row that was not found.
        var updates = await CreateSceneAsync(PolicyType.Update);
        await UpsertAsync(updates, updates.RowA, UserA);
        var landsOnB = () => UpsertAsync(updates, updates.RowB, UserA);
        await landsOnB.Should().ThrowAsync<ForbiddenException>();
        var landsOnBKeepingB = () => UpsertAsync(updates, updates.RowB, UserB);
        await landsOnBKeepingB.Should().ThrowAsync<ForbiddenException>();

        // A new row is an insert, which the policy for updates does not restrict.
        await UpsertAsync(updates, Guid.NewGuid(), UserB);
        (await StoredAsync(updates)).Should().Equal($"b:{UserB}:", $"upserted:{UserA}:", $"upserted:{UserB}:");
    }

    /// <summary>
    /// A table with soft delete does not delete a record by id — it marks the row, with an update.
    /// A policy for deletes binds that too: an update may leave a row marked deleted only if the
    /// policy admits the row. PostgreSQL refuses such an update outright, so the answer is a refusal
    /// where a table without soft delete answers that there was no such row.
    /// </summary>
    [Fact]
    public async Task On_a_table_with_soft_delete_a_delete_policy_binds_marking_a_row_deleted()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid(), "rls_soft", softDelete: true);
        var rowA = await _rls.SeedAsync(table, Row("a", UserA));
        var rowB = await _rls.SeedAsync(table, Row("b", UserB));
        var rowB2 = await _rls.SeedAsync(table, Row("b2", UserB));
        var policy = await _rls.CreatePolicyAsync(table, OwnRowsOnly, "deletes_own", PolicyType.Delete);
        var a = User(table.ProjectId, UserA);

        Task<bool> MarkedDeletedAsync(Guid id) => _rls.ScalarAsSystemAsync<bool>(
            $"""SELECT "_deleted_at" IS NOT NULL FROM "{table.PhysicalName}" WHERE "_id" = '{id}'""");

        (await _rls.NativePoliciesAsync(table)).Should().BeEquivalentTo(
            ["DELETE restrictive USING", "UPDATE restrictive USING WITH CHECK"]);

        var deleteTheirs = () => As(a, () => _rls.Data.DeleteAsync(table.ProjectId, table.LogicalName, rowB));
        await deleteTheirs.Should().ThrowAsync<ForbiddenException>();
        var asNobody = () => As(null, () => _rls.Data.DeleteAsync(table.ProjectId, table.LogicalName, rowA));
        await asNobody.Should().ThrowAsync<ForbiddenException>();
        (await MarkedDeletedAsync(rowB)).Should().BeFalse();
        (await MarkedDeletedAsync(rowA)).Should().BeFalse();

        (await As(a, () => _rls.Data.DeleteAsync(table.ProjectId, table.LogicalName, rowA))).Should().BeTrue();
        (await MarkedDeletedAsync(rowA)).Should().BeTrue();

        // Any other update of a row that is not deleted is not the policy's business.
        await As(a, () => _rls.Data.UpdateAsync(
            table.ProjectId, table.LogicalName, rowB, new Dictionary<string, object?> { ["scratch"] = "changed" }));

        (await As(SecurityContext.Service(table.ProjectId), () => _rls.Data.DeleteAsync(table.ProjectId, table.LogicalName, rowB2)))
            .Should().BeTrue();
        (await MarkedDeletedAsync(rowB2)).Should().BeTrue();

        // Both native policies are one stored policy: in step after a reconcile, gone with it.
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        (await projector.ReconcileAsync(table, TestContext.Current.CancellationToken)).Should().Be(0);
        await _rls.Policies.DeletePolicyAsync(table.ProjectId, policy.Id, TestContext.Current.CancellationToken);
        (await _rls.NativeStateAsync(table)).Names.Should().BeEmpty();
        (await As(a, () => _rls.Data.DeleteAsync(table.ProjectId, table.LogicalName, rowB))).Should().BeTrue();
    }

    // ---- what PostgreSQL is given for each type -------------------------------------------------

    /// <summary>
    /// The command a native policy is for, and the clauses it carries, as PostgreSQL's catalog holds
    /// them. An <c>INSERT</c> finds no row and a <c>SELECT</c> or <c>DELETE</c> leaves none, so
    /// PostgreSQL takes only the one clause for those.
    /// </summary>
    [Theory]
    [InlineData(PolicyType.Select, "SELECT restrictive USING")]
    [InlineData(PolicyType.Insert, "INSERT restrictive WITH CHECK")]
    [InlineData(PolicyType.Update, "UPDATE restrictive USING WITH CHECK")]
    [InlineData(PolicyType.Delete, "DELETE restrictive USING")]
    [InlineData(PolicyType.All, "ALL restrictive USING WITH CHECK")]
    public async Task A_policy_of_each_type_is_one_native_policy_that_comes_and_goes_with_it(PolicyType type, string native)
    {
        var ct = TestContext.Current.CancellationToken;
        var scene = await CreateSceneAsync();
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);

        var policy = await _rls.CreatePolicyAsync(scene.Table, OwnRowsOnly, "own_rows", type);

        (await _rls.Policies.GetPolicyAsync(scene.ProjectId, policy.Id, ct))!.PolicyType.Should().Be(type);
        (await _rls.NativePoliciesAsync(scene.Table)).Should().Equal(native);
        var created = await _rls.NativeStateAsync(scene.Table);
        created.Should().BeEquivalentTo(new { Enabled = true, Forced = true });
        created.Names.Should().HaveCount(2).And.Contain(NativePolicyProjector.AllowPolicyName);

        // In step: reconciling asks PostgreSQL for nothing, and writes no catalog row again.
        (await projector.ReconcileAsync(scene.Table, ct)).Should().Be(0);
        (await projector.ReconcileAsync(scene.Table, ct)).Should().Be(0);
        (await _rls.NativeStateAsync(scene.Table)).Should().BeEquivalentTo(created);

        // Deactivated: nothing of it is left, and nothing is restricted.
        await _rls.Policies.UpdatePolicyAsync(scene.ProjectId, policy.Id, new UpdatePolicyRequest { IsActive = false }, ct);
        var deactivated = await _rls.NativeStateAsync(scene.Table);
        deactivated.Names.Should().BeEmpty();
        deactivated.Should().BeEquivalentTo(new { Enabled = false, Forced = false });
        await As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("theirs", UserB)));
        await ResetAsync(scene);

        // Switched back on it is the same native policy, by the same name.
        await _rls.Policies.UpdatePolicyAsync(scene.ProjectId, policy.Id, new UpdatePolicyRequest { IsActive = true }, ct);
        (await _rls.NativeStateAsync(scene.Table)).Names.Should().Equal(created.Names);

        await _rls.Policies.DeletePolicyAsync(scene.ProjectId, policy.Id, ct);
        var deleted = await _rls.NativeStateAsync(scene.Table);
        deleted.Names.Should().BeEmpty();
        deleted.Should().BeEquivalentTo(new { Enabled = false, Forced = false });
    }

    /// <summary>
    /// A native policy's name carries a hash of what PostgreSQL is asked to enforce — the command
    /// and its clauses as well as the expression — so the same expression under another type is
    /// another policy to create, never one mistaken for being in place already.
    /// </summary>
    [Fact]
    public async Task The_same_expression_under_another_type_is_another_native_policy()
    {
        var scene = await CreateSceneAsync();
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        var id = Guid.NewGuid();
        await using var connection = await _rls.OpenSystemConnectionAsync();

        var planned = new List<NativePolicyProjector.NativePolicy>();
        foreach (var type in Enum.GetValues<PolicyType>())
        {
            var plan = await projector.PlanAsync(
                connection,
                scene.Table,
                [new NativePolicyProjector.StoredPolicy(id, "own_rows", type, OwnRowsOnly, IsActive: true)],
                strictPolicyId: null,
                TestContext.Current.CancellationToken);
            planned.Add(plan.Should().ContainSingle().Which);
        }

        planned.Select(p => p.Name).Should().OnlyHaveUniqueItems();
        planned.Should().OnlyContain(p => p.Name.StartsWith($"morphdb_p_{id:N}_", StringComparison.Ordinal));
        planned.Where(p => p.Using is not null && p.Check is not null).Should().OnlyContain(p => p.Using == p.Check);
    }

    /// <summary>
    /// Policies for writes could be stored by versions that enforced none of them. Start-up hands
    /// them to PostgreSQL like any other stored policy, and from then on they hold.
    /// </summary>
    [Fact]
    public async Task Start_up_enforces_a_write_policy_that_was_stored_and_never_enforced()
    {
        var scene = await CreateSceneAsync();
        await _rls.StoreUnprojectedPolicyAsync(scene.Table, OwnRowsOnly, "stored_insert", PolicyType.Insert);
        await _rls.StoreUnprojectedPolicyAsync(scene.Table, OwnRowsOnly, "stored_delete", PolicyType.Delete);
        await As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("theirs", UserB)));
        await ResetAsync(scene);

        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);

        (await _rls.NativePoliciesAsync(scene.Table)).Should().BeEquivalentTo(
            ["INSERT restrictive WITH CHECK", "DELETE restrictive USING"]);
        var forB = () => As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("theirs", UserB)));
        await forB.Should().ThrowAsync<ForbiddenException>();
        (await As(scene.A, () => _rls.Data.DeleteAsync(scene.ProjectId, scene.Name, scene.RowB))).Should().BeFalse();
        (await As(scene.A, () => _rls.Data.Query(scene.ProjectId).From(scene.Name).CountAsync())).Should().Be(2);

        // A second start changes nothing.
        var state = await _rls.NativeStateAsync(scene.Table);
        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);
        (await _rls.NativeStateAsync(scene.Table)).Should().BeEquivalentTo(state);
    }

    /// <summary>
    /// A stored write policy that cannot be translated is not left unenforced: it admits no row, so
    /// the command it is for is refused to everyone but a caller that bypasses row-level security —
    /// and only that command.
    /// </summary>
    [Fact]
    public async Task Start_up_closes_only_the_command_of_a_write_policy_that_cannot_be_translated()
    {
        var scene = await CreateSceneAsync();
        await _rls.StoreUnprojectedPolicyAsync(scene.Table, "no_such_column = {{user_id}}", "cannot_translate", PolicyType.Insert);

        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);

        var own = () => As(scene.A, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("mine", UserA)));
        await own.Should().ThrowAsync<ForbiddenException>();
        (await As(scene.A, () => _rls.Data.Query(scene.ProjectId).From(scene.Name).CountAsync())).Should().Be(2);
        await As(scene.A, () => _rls.Data.UpdateAsync(
            scene.ProjectId, scene.Name, scene.RowB, new Dictionary<string, object?> { ["scratch"] = "changed" }));
        await As(scene.Bypass, () => _rls.Data.InsertAsync(scene.ProjectId, scene.Name, Row("mine", UserA)));
    }
}
