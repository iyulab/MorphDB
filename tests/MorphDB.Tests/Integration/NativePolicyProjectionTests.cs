using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
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
/// The stored policy rows are the source of truth and PostgreSQL's native policies are derived from
/// them. These tests hold the two together: across a policy's life, at start-up for a database
/// whose policies were stored before they were projected, and when the table under a policy
/// changes.
/// </summary>
[Collection("PostgreSQL")]
public class NativePolicyProjectionTests
{
    private readonly PostgresFixture _fixture;
    private readonly RowLevelSecurityHarness _rls;

    public NativePolicyProjectionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _rls = new RowLevelSecurityHarness(fixture);
    }

    private async Task<(TableMetadata Table, SecurityContext A)> CreateTwoOwnersAsync()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "a", ["owner"] = UserA });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "b", ["owner"] = UserB });
        return (table, User(table.ProjectId, UserA));
    }

    private Task<long> CountAsync(TableMetadata table, SecurityContext? caller) =>
        As(caller, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).CountAsync());

    private Task<long> ExportableRowsAsync(TableMetadata table, SecurityContext? caller) =>
        As(caller, async () => (await _rls.Bulk.StartCsvExportAsync(table.ProjectId, table.LogicalName)).TotalRows);

    // ---- a policy's life -----------------------------------------------------------------------

    [Fact]
    public async Task Creating_a_policy_turns_row_security_on_and_adds_one_native_policy_for_it()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(new { Enabled = false, Forced = false });

        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");

        var state = await _rls.NativeStateAsync(table);
        state.Enabled.Should().BeTrue();
        state.Forced.Should().BeTrue("the service role owns the table, and an owner is exempt unless forced");
        state.Names.Should().HaveCount(2);
        state.Names.Should().Contain(NativePolicyProjector.AllowPolicyName);
        state.Names.Should().ContainSingle(n => n.StartsWith($"morphdb_p_{policy.Id:N}_", StringComparison.Ordinal));
        (await CountAsync(table, a)).Should().Be(1);
    }

    [Fact]
    public async Task Deactivating_a_policy_makes_every_row_readable_again_and_leaves_nothing_behind()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");
        (await CountAsync(table, null)).Should().Be(0);

        await _rls.Policies.UpdatePolicyAsync(table.ProjectId, policy.Id, new UpdatePolicyRequest { IsActive = false }, TestContext.Current.CancellationToken);

        (await CountAsync(table, a)).Should().Be(2);
        (await CountAsync(table, null)).Should().Be(2);
        var state = await _rls.NativeStateAsync(table);
        state.ProjectedNames.Should().BeEmpty();
        state.Should().BeEquivalentTo(new { Enabled = false, Forced = false });

        // And back.
        await _rls.Policies.UpdatePolicyAsync(table.ProjectId, policy.Id, new UpdatePolicyRequest { IsActive = true }, TestContext.Current.CancellationToken);
        (await CountAsync(table, a)).Should().Be(1);
        (await _rls.NativeStateAsync(table)).ProjectedNames.Should().HaveCount(2);
    }

    [Fact]
    public async Task Deleting_a_policy_makes_every_row_readable_again_and_leaves_nothing_behind()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");

        await _rls.Policies.DeletePolicyAsync(table.ProjectId, policy.Id, TestContext.Current.CancellationToken);

        (await CountAsync(table, a)).Should().Be(2);
        (await CountAsync(table, null)).Should().Be(2);
        var state = await _rls.NativeStateAsync(table);
        state.ProjectedNames.Should().BeEmpty();
        state.Should().BeEquivalentTo(new { Enabled = false, Forced = false });
    }

    [Fact]
    public async Task Deleting_one_of_two_policies_keeps_the_other_enforced()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "owner_only");
        var second = await _rls.CreatePolicyAsync(table, "title = 'b'", "b_only");
        (await CountAsync(table, a)).Should().Be(0, "A owns 'a', and only 'b' passes the second policy");

        await _rls.Policies.DeletePolicyAsync(table.ProjectId, second.Id, TestContext.Current.CancellationToken);

        (await CountAsync(table, a)).Should().Be(1);
        var state = await _rls.NativeStateAsync(table);
        state.ProjectedNames.Should().HaveCount(2);
        state.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_a_policys_expression_replaces_its_native_policy()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");
        var before = (await _rls.NativeStateAsync(table)).Names;

        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Expression = "owner <> {{user_id}}" }, TestContext.Current.CancellationToken);

        var after = (await _rls.NativeStateAsync(table)).Names;
        after.Should().HaveCount(2);
        after.Should().NotBeEquivalentTo(before, "the name follows the expression");
        (await As(a, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync()))
            .Should().ContainSingle().Which["title"].Should().Be("b");

        // A change that is not to the expression changes nothing PostgreSQL holds.
        var settled = await _rls.NativeStateAsync(table);
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Name = "renamed", Description = "described" }, TestContext.Current.CancellationToken);
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(settled);
    }

    [Fact]
    public async Task Reconciling_a_table_that_is_already_in_step_issues_no_statement()
    {
        var (table, _) = await CreateTwoOwnersAsync();
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        var before = await _rls.NativeStateAsync(table);

        (await projector.ReconcileAsync(table, TestContext.Current.CancellationToken)).Should().Be(0);
        (await projector.ReconcileAsync(table, TestContext.Current.CancellationToken)).Should().Be(0);

        // The catalog rows were not written again either.
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(before);
    }

    // ---- what is refused -----------------------------------------------------------------------

    [Theory]
    [InlineData("no_such_column = {{user_id}}", "a column the table does not have")]
    [InlineData("owner = {{no_such_placeholder}}", "a placeholder that does not exist")]
    [InlineData("owner = '{{user_id}}'", "a placeholder inside a string is text, not the caller")]
    [InlineData("owner = $1", "a parameter nothing supplies")]
    [InlineData("owner_id = {{user_id}} || 'x'", "a comparison PostgreSQL cannot type")]
    [InlineData("owner", "not a predicate")]
    public async Task A_policy_that_cannot_be_enforced_is_refused_and_leaves_nothing(string expression, string why)
    {
        var (table, a) = await CreateTwoOwnersAsync();

        var act = () => _rls.CreatePolicyAsync(table, expression);

        (await act.Should().ThrowAsync<SchemaException>(why)).Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
        (await _rls.StoredPolicyCountAsync(table)).Should().Be(0);
        var state = await _rls.NativeStateAsync(table);
        state.Names.Should().BeEmpty();
        state.Enabled.Should().BeFalse();
        (await CountAsync(table, a)).Should().Be(2, "the table reads as it did");
    }

    [Fact]
    public async Task A_refused_policy_does_not_disturb_the_ones_the_table_already_has()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var kept = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "kept");
        var before = await _rls.NativeStateAsync(table);

        var duplicate = () => _rls.CreatePolicyAsync(table, "title = 'a'", "kept");
        await duplicate.Should().ThrowAsync<DuplicateNameException>();

        var broken = () => _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, kept.Id, new UpdatePolicyRequest { Expression = "no_such_column = {{user_id}}" });
        (await broken.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_EXPRESSION");

        (await _rls.StoredPolicyCountAsync(table)).Should().Be(1);
        (await _rls.Policies.GetPolicyAsync(table.ProjectId, kept.Id, TestContext.Current.CancellationToken))!.Expression.Should().Be("owner = {{user_id}}");
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(before);
        (await CountAsync(table, a)).Should().Be(1);
    }

    /// <summary>
    /// Switching a policy on is asking for it to be enforced. One that cannot be is refused there,
    /// rather than switched on as a policy that closes the table.
    /// </summary>
    [Fact]
    public async Task Activating_a_policy_that_cannot_be_enforced_is_refused()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policyId = await _rls.StoreUnprojectedPolicyAsync(table, "no_such_column = {{user_id}}");
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policyId, new UpdatePolicyRequest { IsActive = false }, TestContext.Current.CancellationToken);

        var activate = () => _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policyId, new UpdatePolicyRequest { IsActive = true });

        (await activate.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
        (await _rls.Policies.GetPolicyAsync(table.ProjectId, policyId, TestContext.Current.CancellationToken))!
            .IsActive.Should().BeFalse();
        (await _rls.NativeStateAsync(table)).Names.Should().BeEmpty();
        (await ExportableRowsAsync(table, a)).Should().Be(2);
    }

    // ---- start-up ------------------------------------------------------------------------------

    [Fact]
    public async Task Start_up_projects_a_policy_that_was_stored_but_never_projected()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policyId = await _rls.StoreUnprojectedPolicyAsync(table, "owner = {{user_id}}");
        (await CountAsync(table, null)).Should().Be(2, "nothing enforces the stored row yet");

        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);

        var state = await _rls.NativeStateAsync(table);
        state.Enabled.Should().BeTrue();
        state.Names.Should().ContainSingle(n => n.StartsWith($"morphdb_p_{policyId:N}_", StringComparison.Ordinal));
        (await CountAsync(table, a)).Should().Be(1);
        (await CountAsync(table, null)).Should().Be(0);

        // A second start changes nothing.
        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(state);
    }

    /// <summary>
    /// A stored policy start-up cannot translate is not left unenforced, and not allowed to stop the
    /// service either: its table is closed to everyone but a caller that bypasses row-level
    /// security, and the log says which policy and why.
    /// </summary>
    [Theory]
    [InlineData("no_such_column = {{user_id}}")]
    [InlineData("true) OR (1=1")]
    public async Task Start_up_closes_a_table_whose_stored_policy_cannot_be_translated(string expression)
    {
        var (table, a) = await CreateTwoOwnersAsync();
        await _rls.StoreUnprojectedPolicyAsync(table, expression, "cannot_translate");
        var log = new RecordingLogger();

        await _rls.StartUpAsync(log);

        (await ExportableRowsAsync(table, a)).Should().Be(0);
        (await ExportableRowsAsync(table, null)).Should().Be(0);
        (await ExportableRowsAsync(table, SecurityContext.Service(table.ProjectId))).Should().Be(2);

        // Listing, aggregating and reading one record used to write the policy into their own WHERE
        // clause, and threw on one they could not write out. They are filtered by PostgreSQL alone
        // now, and answer what every other read answers: no row.
        var storedId = (Guid)(await As(SecurityContext.Service(table.ProjectId), () =>
            _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync()))[0][SystemColumns.Id]!;
        var counting = new AggregationRequest
        {
            Aggregations = [new AggregationColumn { Function = AggregateFunction.Count, Alias = "n" }]
        };

        foreach (var caller in new[] { a, null })
        {
            (await As(caller, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync()))
                .Should().BeEmpty();
            (await CountAsync(table, caller)).Should().Be(0);
            var counted = await As(caller, () => _rls.Aggregation.AggregateAsync(table.ProjectId, table.LogicalName, counting));
            Convert.ToInt64(counted.Data[0]["n"], System.Globalization.CultureInfo.InvariantCulture).Should().Be(0);
            (await As(caller, () => _rls.Data.GetByIdAsync(table.ProjectId, table.LogicalName, storedId))).Should().BeNull();
        }

        (await CountAsync(table, SecurityContext.Service(table.ProjectId))).Should().Be(2);
        (await _rls.StoredAsync(table, storedId)).Should().NotBeNull();
        log.Warnings.Should().ContainSingle(w => w.Contains(table.LogicalName) && w.Contains("cannot_translate"))
            .Which.Should().Contain(expression);

        // Correcting the policy opens the table to the rows it admits.
        var stored = (await _rls.Policies.GetPoliciesAsync(table.ProjectId, table.TableId, TestContext.Current.CancellationToken)).Single();
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, stored.Id, new UpdatePolicyRequest { Expression = "owner = {{user_id}}" }, TestContext.Current.CancellationToken);
        (await CountAsync(table, a)).Should().Be(1);
    }

    [Fact]
    public async Task Start_up_projects_a_stored_policy_for_every_command_as_one()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        await _rls.StoreUnprojectedPolicyAsync(table, "owner = {{user_id}}", type: PolicyType.All);

        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);

        (await CountAsync(table, a)).Should().Be(1);
        var forB = () => As(a, () => _rls.Data.InsertAsync(
            table.ProjectId, table.LogicalName, new Dictionary<string, object?> { ["title"] = "c", ["owner"] = UserB }));
        await forB.Should().ThrowAsync<ForbiddenException>();
    }

    /// <summary>
    /// A materialized view over a table with a policy can no longer be created, but a database an
    /// earlier version served may hold one — its copy filled past the policy. Start-up drops
    /// nothing; it names the pair, and from then on the view is not read or refreshed until one of
    /// the two is gone.
    /// </summary>
    [Fact]
    public async Task Start_up_reports_a_materialized_view_over_a_policy_and_the_view_is_not_read_while_both_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var (table, a) = await CreateTwoOwnersAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        CreateViewRequest Titles(string name, bool materialized) => new()
        {
            ProjectId = table.ProjectId,
            Name = name,
            IsMaterialized = materialized,
            Definition = new ViewDefinition
            {
                BaseTable = table.LogicalName,
                Columns = [new ViewColumnSpec { Source = "title", Alias = "title" }]
            }
        };

        // As an earlier version left it: the views first, built while nothing forbade them, then a
        // policy stored without a look at what reads the table.
        var direct = await _rls.Views.CreateViewAsync(Titles("mv_direct_" + suffix, materialized: true), ct);
        var plain = await _rls.Views.CreateViewAsync(Titles("v_plain_" + suffix, materialized: false), ct);
        var throughView = "mv_through_" + suffix;
        await _rls.ExecuteAsSystemAsync(
            $"""CREATE MATERIALIZED VIEW "{throughView}" AS SELECT * FROM "{plain.PhysicalName}" """);
        var policyId = await _rls.StoreUnprojectedPolicyAsync(table, "owner = {{user_id}}", "owner_only");

        Task<ViewQueryResult> ReadAsync(string view, SecurityContext? caller) => As(caller, () => _rls.Views.QueryViewAsync(
            new ViewQueryRequest { ProjectId = table.ProjectId, ViewName = view }));

        var log = new RecordingLogger();
        await _rls.StartUpAsync(log);

        // Each pair is named: the view, the table and the policy — a view reached through a plain
        // view too, by the only name it has.
        log.Warnings.Should().ContainSingle(w =>
            w.Contains($"'{direct.LogicalName}'") && w.Contains($"'{table.LogicalName}'") && w.Contains("'owner_only'"));
        log.Warnings.Should().ContainSingle(w =>
            w.Contains($"'{throughView}'") && w.Contains($"'{table.LogicalName}'") && w.Contains("'owner_only'"));

        // Nothing was dropped, and the policy is enforced where it can be.
        (await _rls.RelationExistsAsync(direct.PhysicalName)).Should().BeTrue();
        (await _rls.RelationExistsAsync(throughView)).Should().BeTrue();
        (await _rls.StoredPolicyCountAsync(table)).Should().Be(1);
        (await CountAsync(table, a)).Should().Be(1);
        (await ReadAsync(plain.LogicalName, a)).TotalCount.Should().Be(1, "a plain view is read through the policy");

        // The copy is not handed out — to anyone — and not filled again.
        foreach (var caller in new[] { a, null, SecurityContext.Service(table.ProjectId) })
        {
            var read = () => ReadAsync(direct.LogicalName, caller);
            var refusal = (await read.Should().ThrowAsync<SchemaException>()).Which;
            refusal.ErrorCode.Should().Be("INVALID_OPERATION");
            refusal.Message.Should().Contain($"'{direct.LogicalName}'").And.Contain($"'{table.LogicalName}'")
                .And.Contain("'owner_only'").And.Contain("plain");
        }

        var refresh = () => _rls.Views.RefreshMaterializedViewAsync(direct.ViewId);
        (await refresh.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_OPERATION");

        // A second policy is refused like a first, and says which views stand in the way.
        var second = () => _rls.CreatePolicyAsync(table, "title = 'a'", "a_only");
        (await second.Should().ThrowAsync<SchemaException>()).Which.Message
            .Should().Contain($"'{direct.LogicalName}'").And.Contain($"'{throughView}'");

        // Deleting the policy ends it: the view reads again, and the next start has nothing to say.
        await _rls.Policies.DeletePolicyAsync(table.ProjectId, policyId, ct);
        (await ReadAsync(direct.LogicalName, a)).TotalCount.Should().Be(2);
        await _rls.Views.RefreshMaterializedViewAsync(direct.ViewId, cancellationToken: ct);

        var quiet = new RecordingLogger();
        await _rls.StartUpAsync(quiet);
        quiet.Warnings.Should().NotContain(w => w.Contains(direct.LogicalName) || w.Contains(throughView));

        await _rls.ExecuteAsSystemAsync($"""DROP MATERIALIZED VIEW "{throughView}" """);
    }

    // ---- the table under a policy ---------------------------------------------------------------

    [Fact]
    public async Task A_table_with_a_policy_can_be_deleted()
    {
        var (table, _) = await CreateTwoOwnersAsync();
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}");

        await _rls.Schema.DeleteTableAsync(table.TableId, TestContext.Current.CancellationToken);

        (await _rls.Schema.GetTableByIdAsync(table.TableId, TestContext.Current.CancellationToken)).Should().BeNull();

        // Its stored policy rows outlive it, and start-up passes over them.
        (await _rls.StoredPolicyCountAsync(table)).Should().Be(1);
        await _rls.StartUpAsync(NullLogger<PostgresSchemaLayerService>.Instance);
    }

    [Fact]
    public async Task A_column_a_policy_reads_cannot_be_deleted_until_the_policy_lets_go_of_it()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "owner_only");
        var owner = table.Columns.Single(c => c.LogicalName == "owner");

        var act = () => _rls.Schema.DeleteColumnAsync(owner.ColumnId, TestContext.Current.CancellationToken);

        var refusal = (await act.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");
        refusal.Message.Should().Contain("'owner'").And.Contain("'owner_only'");
        refusal.Message.Should().NotContain(owner.PhysicalName).And.NotContain("morphdb_p_");
        (await CountAsync(table, a)).Should().Be(1, "the column and the policy are both still there");

        // A column the policy does not read is not held.
        await _rls.Schema.DeleteColumnAsync(table.Columns.Single(c => c.LogicalName == "scratch").ColumnId, TestContext.Current.CancellationToken);

        await _rls.Policies.DeletePolicyAsync(table.ProjectId, policy.Id, TestContext.Current.CancellationToken);
        await _rls.Schema.DeleteColumnAsync(owner.ColumnId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_column_a_policy_reads_cannot_be_given_another_type()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        await _rls.CreatePolicyAsync(table, "owner = {{user_id}}", "owner_only");
        var owner = table.Columns.Single(c => c.LogicalName == "owner");
        var current = (await _rls.Schema.GetTableByIdAsync(table.TableId, TestContext.Current.CancellationToken))!;

        var act = () => _rls.Schema.UpdateColumnAsync(new UpdateColumnRequest
        {
            ColumnId = owner.ColumnId,
            DataType = MorphDataType.Json,
            ForceCast = true,
            ExpectedVersion = current.SchemaVersion
        });

        var refusal = (await act.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");
        refusal.Message.Should().Contain("'owner'").And.Contain("'owner_only'");
        (await CountAsync(table, a)).Should().Be(1);
    }

    // ---- renaming a column a policy reads -------------------------------------------------------

    /// <summary>What each kind of caller reads from the table, by title.</summary>
    private async Task<string[]> VisibleToEachCallerAsync(TableMetadata table)
    {
        var callers = new (string Who, SecurityContext? Caller)[]
        {
            ("A", User(table.ProjectId, UserA)),
            ("B", User(table.ProjectId, UserB)),
            ("no caller", null),
            ("bypass", SecurityContext.Service(table.ProjectId)),
        };

        var seen = new List<string>();
        foreach (var (who, caller) in callers)
        {
            var rows = await As(caller, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync());
            seen.Add($"{who}: {string.Join(",", rows.Select(r => (string)r["title"]!).Order(StringComparer.Ordinal))}");
        }

        return [.. seen];
    }

    private async Task<TableMetadata> CurrentAsync(TableMetadata table) =>
        (await _rls.Metadata.GetTableByIdAsync(table.TableId, includeColumns: true, TestContext.Current.CancellationToken))!;

    private async Task<string> StoredExpressionAsync(TableMetadata table, Guid policyId) =>
        (await _rls.Policies.GetPolicyAsync(table.ProjectId, policyId, TestContext.Current.CancellationToken))!.Expression;

    /// <summary>
    /// A policy is stored in the table's logical column names. Giving a column it reads another
    /// name renames it in the stored expression too — where it is an identifier, and nowhere else —
    /// and asks nothing of PostgreSQL, whose policy holds the column rather than its name.
    /// </summary>
    [Fact]
    public async Task Renaming_a_column_a_policy_reads_renames_it_in_the_stored_policy_and_changes_nothing_in_PostgreSQL()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(
            table, "owner = {{user_id}} AND \"owner\" IS NOT NULL AND title <> 'owner' AND owner_id IS NULL");
        var before = await _rls.NativeStateAsync(table);
        var visible = await VisibleToEachCallerAsync(table);
        visible.Should().Equal("A: a", "B: b", "no caller: ", "bypass: a,b");

        await _rls.Schema.UpdateColumnAsync(new UpdateColumnRequest
        {
            ColumnId = table.Columns.Single(c => c.LogicalName == "owner").ColumnId,
            LogicalName = "holder",
            ExpectedVersion = (await CurrentAsync(table)).SchemaVersion
        }, TestContext.Current.CancellationToken);

        // The identifier, bare or quoted — not the string literal, and not the longer name.
        (await StoredExpressionAsync(table, policy.Id))
            .Should().Be("holder = {{user_id}} AND holder IS NOT NULL AND title <> 'owner' AND owner_id IS NULL");

        // Reconciling finds nothing to do: the same policies, and not one catalog row rewritten.
        var renamed = await CurrentAsync(table);
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        (await projector.ReconcileAsync(renamed, TestContext.Current.CancellationToken)).Should().Be(0);
        var after = await _rls.NativeStateAsync(renamed);
        after.Names.Should().Equal(before.Names);
        after.Versions.Should().Equal(before.Versions);
        (await VisibleToEachCallerAsync(renamed)).Should().Equal(visible);

        // The policy is still one the service can change, in the column's new name only.
        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Description = "described" }, TestContext.Current.CancellationToken);
        (await _rls.NativeStateAsync(renamed)).Names.Should().Equal(before.Names);

        var oldName = () => _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Expression = "owner = {{user_id}}" });
        (await oldName.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("INVALID_EXPRESSION");

        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Expression = "holder <> {{user_id}}" }, TestContext.Current.CancellationToken);
        (await As(a, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync()))
            .Should().ContainSingle().Which["title"].Should().Be("b");
    }

    /// <summary>
    /// <c>RenameColumnAsync</c> renames the physical column as well. PostgreSQL's policy follows the
    /// column through that, so no caller reads anything else at any point; the native policy's name
    /// carries a hash of the physical expression, so the next reconcile replaces that one policy
    /// with one of the same meaning under its new name, and the one after finds nothing to do.
    /// </summary>
    [Fact]
    public async Task Renaming_the_physical_column_too_leaves_every_caller_reading_what_it_read()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "owner = {{user_id}} AND title <> 'owner'");
        var owner = table.Columns.Single(c => c.LogicalName == "owner");
        var before = await _rls.NativeStateAsync(table);
        var visible = await VisibleToEachCallerAsync(table);
        visible.Should().Equal("A: a", "B: b", "no caller: ", "bypass: a,b");

        await _rls.Schema.RenameColumnAsync(new RenameColumnRequest
        {
            ColumnId = owner.ColumnId,
            NewLogicalName = "holder",
            ExpectedVersion = (await CurrentAsync(table)).SchemaVersion
        }, TestContext.Current.CancellationToken);

        var renamed = await CurrentAsync(table);
        renamed.Columns.Single(c => c.LogicalName == "holder").PhysicalName.Should().NotBe(owner.PhysicalName);
        (await StoredExpressionAsync(table, policy.Id)).Should().Be("holder = {{user_id}} AND title <> 'owner'");

        // Before anything is reconciled: the policy PostgreSQL holds went with the column.
        (await _rls.NativeStateAsync(renamed)).Names.Should().Equal(before.Names);
        (await VisibleToEachCallerAsync(renamed)).Should().Equal(visible);

        // The reconcile that follows creates the policy under its new name and drops the old one.
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        (await projector.ReconcileAsync(renamed, TestContext.Current.CancellationToken)).Should().Be(2);
        var after = await _rls.NativeStateAsync(renamed);
        after.Names.Should().HaveCount(2).And.Contain(NativePolicyProjector.AllowPolicyName);
        after.Names.Should().ContainSingle(n => n.StartsWith($"morphdb_p_{policy.Id:N}_", StringComparison.Ordinal))
            .Which.Should().NotBe(before.Names.Single(n => n.StartsWith("morphdb_p_", StringComparison.Ordinal)));
        (await VisibleToEachCallerAsync(renamed)).Should().Equal(visible);
        (await projector.ReconcileAsync(renamed, TestContext.Current.CancellationToken)).Should().Be(0);

        await _rls.Policies.UpdatePolicyAsync(
            table.ProjectId, policy.Id, new UpdatePolicyRequest { Expression = "holder <> {{user_id}}" }, TestContext.Current.CancellationToken);
        (await As(a, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync()))
            .Should().ContainSingle().Which["title"].Should().Be("b");
    }

    /// <summary>
    /// A column may be called what a placeholder is called. Renaming the column must leave the
    /// placeholder alone: <c>{{role}}</c> is the caller's role, not the column.
    /// </summary>
    [Fact]
    public async Task Renaming_a_column_named_like_a_placeholder_leaves_the_placeholder_alone()
    {
        var table = await _rls.CreateDocumentsAsync(Guid.NewGuid());
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "s", ["department"] = "sales" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "e", ["department"] = "engineering" });
        var department = table.Columns.Single(c => c.LogicalName == "department");

        async Task RenameAsync(string to) => await _rls.Schema.UpdateColumnAsync(new UpdateColumnRequest
        {
            ColumnId = department.ColumnId,
            LogicalName = to,
            ExpectedVersion = (await CurrentAsync(table)).SchemaVersion
        });

        async Task<string[]> SeenBySalesAsync()
        {
            var inSales = User(table.ProjectId, UserA);
            inSales.Role = "sales";
            var rows = await As(inSales, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync());
            return [.. rows.Select(r => (string)r["title"]!)];
        }

        await RenameAsync("role");
        var policy = await _rls.CreatePolicyAsync(table, "role = {{role}}");
        (await SeenBySalesAsync()).Should().Equal("s");

        await RenameAsync("team");

        (await StoredExpressionAsync(table, policy.Id)).Should().Be("team = {{role}}");
        (await SeenBySalesAsync()).Should().Equal("s");

        // A name that is not a plain identifier is written the only way a policy can name it.
        await RenameAsync("sales team");
        (await StoredExpressionAsync(table, policy.Id)).Should().Be("\"sales team\" = {{role}}");
        var projector = new NativePolicyProjector(_fixture.Connections, _rls.Metadata, NullLogger.Instance);
        (await projector.ReconcileAsync(await CurrentAsync(table), TestContext.Current.CancellationToken)).Should().Be(0);
        (await SeenBySalesAsync()).Should().Equal("s");
    }

    /// <summary>
    /// A policy reads a name as a column whenever the table has a column of that name. A column
    /// that took a name a policy already uses for a function would change what the policy means,
    /// so that rename is refused and nothing changes.
    /// </summary>
    [Fact]
    public async Task A_column_cannot_take_a_name_a_policy_uses_for_something_else()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        var policy = await _rls.CreatePolicyAsync(table, "lower(owner) = {{user_id}}", "owner_only");
        var before = await _rls.NativeStateAsync(table);
        var scratch = table.Columns.Single(c => c.LogicalName == "scratch");

        var update = () => _rls.Schema.UpdateColumnAsync(new UpdateColumnRequest
        {
            ColumnId = scratch.ColumnId,
            LogicalName = "lower",
            ExpectedVersion = table.SchemaVersion
        });
        var refusal = (await update.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");
        refusal.Message.Should().Contain("'scratch'").And.Contain("'lower'").And.Contain("'owner_only'");

        var rename = () => _rls.Schema.RenameColumnAsync(new RenameColumnRequest
        {
            ColumnId = scratch.ColumnId,
            NewLogicalName = "lower",
            ExpectedVersion = table.SchemaVersion
        });
        (await rename.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");

        // Neither the column nor the policy moved — the physical column included.
        var current = await CurrentAsync(table);
        current.Columns.Single(c => c.ColumnId == scratch.ColumnId).Should().BeEquivalentTo(
            new { LogicalName = "scratch", scratch.PhysicalName });
        (await StoredExpressionAsync(table, policy.Id)).Should().Be("lower(owner) = {{user_id}}");
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(before);
        (await CountAsync(table, a)).Should().Be(1);
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["title"] = "c", ["scratch"] = "still a column" });
    }

    /// <summary>
    /// Adding a column is the other way a name becomes a column. A stored column would change what
    /// the policy means; a derived one would leave a policy nothing can translate, closing the table
    /// to every caller. Both are refused, and the table is left as it was.
    /// </summary>
    [Fact]
    public async Task A_column_a_policy_names_as_something_else_cannot_be_added()
    {
        var (table, a) = await CreateTwoOwnersAsync();
        await _rls.CreatePolicyAsync(table, "lower(owner) = {{user_id}}", "owner_only");
        var before = await _rls.NativeStateAsync(table);
        var current = await CurrentAsync(table);

        var stored = () => _rls.Schema.AddColumnAsync(new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "lower",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = current.SchemaVersion
        }, TestContext.Current.CancellationToken);
        var refusal = (await stored.Should().ThrowAsync<SchemaException>()).Which;
        refusal.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");
        refusal.Message.Should().Contain("'lower'").And.Contain("'owner_only'");

        var derived = () => _rls.Schema.AddColumnAsync(new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "lower",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = current.SchemaVersion,
            FormulaConfig = new FormulaColumnConfig { Formula = "{title}", ReturnType = MorphDataType.Text }
        }, TestContext.Current.CancellationToken);
        (await derived.Should().ThrowAsync<SchemaException>()).Which.ErrorCode.Should().Be("COLUMN_USED_BY_POLICY");

        (await CurrentAsync(table)).Columns.Should().NotContain(c => c.LogicalName == "lower");
        (await _rls.NativeStateAsync(table)).Should().BeEquivalentTo(before);
        (await CountAsync(table, a)).Should().Be(1);

        // A name no policy uses is added as before.
        await _rls.Schema.AddColumnAsync(new AddColumnRequest
        {
            TableId = table.TableId,
            LogicalName = "note",
            DataType = MorphDataType.Text,
            IsNullable = true,
            ExpectedVersion = current.SchemaVersion
        }, TestContext.Current.CancellationToken);
        (await CountAsync(table, a)).Should().Be(1);
    }

    private sealed class RecordingLogger : ILogger<PostgresSchemaLayerService>
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyCollection<string> Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                _warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
