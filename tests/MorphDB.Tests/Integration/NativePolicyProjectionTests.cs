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

        // Counted the way an export counts — a statement of its own, which nothing but PostgreSQL
        // filters.
        (await ExportableRowsAsync(table, a)).Should().Be(0);
        (await ExportableRowsAsync(table, null)).Should().Be(0);
        (await ExportableRowsAsync(table, SecurityContext.Service(table.ProjectId))).Should().Be(2);

        // The reads that also apply the policy to their own WHERE clause still refuse to run one
        // they cannot write out, as they did before PostgreSQL enforced anything.
        var list = () => CountAsync(table, a);
        await list.Should().ThrowAsync<Exception>();
        (await CountAsync(table, null)).Should().Be(0);
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
