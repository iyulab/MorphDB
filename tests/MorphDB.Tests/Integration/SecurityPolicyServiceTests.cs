using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;
using static MorphDB.Tests.Integration.RowLevelSecurityHarness;

namespace MorphDB.Tests.Integration;

/// <summary>
/// The policy service resolves a table by the name a caller gives it, and it had no test of any
/// kind. Both name lookups asked _morph_tables for a column called "name", which that table has
/// never had — so every create and every by-name read answered 42703, surfaced as a 500. The
/// endpoints were shipped and unreachable at the same time, which is the failure a suite with no
/// coverage of a service cannot report.
/// </summary>
[Collection("PostgreSQL")]
public class SecurityPolicyServiceTests
{
    private readonly RowLevelSecurityHarness _rls;
    private readonly SecurityPolicyService _policies;
    private readonly PostgresSchemaManager _schemaManager;

    public SecurityPolicyServiceTests(PostgresFixture fixture)
    {
        _rls = new RowLevelSecurityHarness(fixture);
        _policies = _rls.Policies;
        _schemaManager = _rls.Schema;
    }

    /// <summary>The owners of the rows a caller reads from the table, in order.</summary>
    private async Task<List<string?>> OwnersReadByAsync(TableMetadata table, SecurityContext? caller)
    {
        var rows = await As(caller, () => _rls.Data.Query(table.ProjectId).From(table.LogicalName).SelectAll().ToListAsync());
        return [.. rows.Select(r => (string?)r["owner_id"]).Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task A_policy_can_be_created_for_a_table_and_read_back_by_that_table_name()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_target_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var created = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "owner_reads_only",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id = {{user_id}}"
        }, TestContext.Current.CancellationToken);

        created.TableId.Should().NotBeEmpty("the policy must resolve the table it applies to");

        var byName = await _policies.GetPoliciesByTableNameAsync(projectId, tableName, TestContext.Current.CancellationToken);
        byName.Should().ContainSingle(p => p.Name == "owner_reads_only");

        var byId = await _policies.GetPoliciesAsync(projectId, created.TableId, TestContext.Current.CancellationToken);
        byId.Should().ContainSingle(p => p.Id == created.Id);
    }

    [Fact]
    public async Task Naming_a_table_that_does_not_exist_is_the_callers_mistake()
    {
        var act = () => _policies.CreatePolicyAsync(Guid.NewGuid(), new CreatePolicyRequest
        {
            Name = "orphan_policy",
            TableName = "no_such_table_" + Guid.NewGuid().ToString("N")[..8],
            PolicyType = PolicyType.Select,
            Expression = "true"
        });

        await act.Should().ThrowAsync<TableNotFoundException>();
    }

    /// <summary>
    /// A deleted table's name is free again, and a policy attached to the tombstone is not a policy
    /// on the live table that took the name back.
    /// </summary>
    [Fact]
    public async Task A_deleted_tables_policies_do_not_follow_its_name_to_the_table_that_replaces_it()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_recreated_" + Guid.NewGuid().ToString("N")[..8];
        var first = await CreateTableAsync(projectId, tableName);

        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "policy_on_the_first_table",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "true"
        }, TestContext.Current.CancellationToken);

        await _schemaManager.DeleteTableAsync(first.TableId, TestContext.Current.CancellationToken);
        await CreateTableAsync(projectId, tableName);

        var policies = await _policies.GetPoliciesByTableNameAsync(projectId, tableName, TestContext.Current.CancellationToken);
        policies.Should().BeEmpty("the name now belongs to a different table");
    }

    /// <summary>
    /// A policy expression becomes part of a policy PostgreSQL evaluates, which makes it a
    /// caller-authored string reaching SQL verbatim — the category the constitution says must never
    /// pass unchecked. CHECK predicates and index predicates were gated; this path shipped open.
    /// </summary>
    [Theory]
    [InlineData("true; DROP TABLE morphdb._morph_tables", "a statement separator ends the query early")]
    [InlineData("true) OR (1=1", "an unbalanced parenthesis escapes the clause it is emitted into")]
    [InlineData("true -- ", "a comment truncates whatever follows the expression")]
    [InlineData("owner_id = 'unterminated", "an unterminated quote swallows the rest of the statement")]
    public async Task An_expression_that_could_escape_its_clause_is_refused(string expression, string why)
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_guard_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var act = () => _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "hostile",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = expression
        });

        (await act.Should().ThrowAsync<SchemaException>(why))
            .Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
    }

    [Fact]
    public async Task An_ordinary_predicate_still_passes_the_gate()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_ok_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var created = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "owner_scope",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id = {{user_id}} AND (owner_id IS NOT NULL)"
        }, TestContext.Current.CancellationToken);

        created.Expression.Should().Contain("{{user_id}}", "the placeholder is substituted at read time");
    }

    [Fact]
    public async Task Updating_a_policy_rewrites_it_and_still_refuses_an_escaping_expression()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_update_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var created = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "before",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "true"
        }, TestContext.Current.CancellationToken);

        var updated = await _policies.UpdatePolicyAsync(projectId, created.Id, new UpdatePolicyRequest
        {
            Name = "after",
            Expression = "owner_id IS NOT NULL"
        }, TestContext.Current.CancellationToken);

        updated.Name.Should().Be("after");
        (await _policies.GetPolicyAsync(projectId, created.Id, TestContext.Current.CancellationToken))!.Expression.Should().Be("owner_id IS NOT NULL");

        var act = () => _policies.UpdatePolicyAsync(projectId, created.Id, new UpdatePolicyRequest
        {
            Expression = "true; DROP TABLE morphdb._morph_tables"
        });
        await act.Should().ThrowAsync<SchemaException>("the update door is the same door");
    }

    [Fact]
    public async Task A_deleted_policy_stops_applying()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_delete_" + Guid.NewGuid().ToString("N")[..8];
        var table = await CreateTableAsync(projectId, tableName);
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["owner_id"] = null });
        var caller = User(projectId, UserA);

        var created = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "temporary",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id IS NOT NULL"
        }, TestContext.Current.CancellationToken);
        (await OwnersReadByAsync(table, caller)).Should().BeEmpty("the policy hides the row");

        await _policies.DeletePolicyAsync(projectId, created.Id, TestContext.Current.CancellationToken);

        (await _policies.GetPolicyAsync(projectId, created.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await OwnersReadByAsync(table, caller)).Should().HaveCount(1, "no policy remains to constrain the read");
    }

    /// <summary>
    /// A caller's identity reaches the policy as a value. A quote inside it stays inside it: the
    /// caller is compared with the column, and never writes the predicate.
    /// </summary>
    [Fact]
    public async Task A_caller_identity_carrying_a_quote_is_compared_as_a_value()
    {
        const string hostile = "x' OR '1'='1";
        var projectId = Guid.NewGuid();
        var tableName = "policy_escape_" + Guid.NewGuid().ToString("N")[..8];
        var table = await CreateTableAsync(projectId, tableName);
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["owner_id"] = "someone-else" });
        await _rls.SeedAsync(table, new Dictionary<string, object?> { ["owner_id"] = hostile });

        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "owner_only",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id = {{user_id}}"
        }, TestContext.Current.CancellationToken);

        (await OwnersReadByAsync(table, User(projectId, hostile)))
            .Should().Equal([hostile], "the only row it reads is the one that carries exactly that text as its owner");
    }

    /// <summary>
    /// A write policy used to be stored and then consulted by nothing — inserts, updates and deletes
    /// never read one. A security rule that silently does not apply is refused instead, until writes
    /// enforce it.
    /// </summary>
    [Theory]
    [InlineData(PolicyType.Insert)]
    [InlineData(PolicyType.Update)]
    [InlineData(PolicyType.Delete)]
    [InlineData(PolicyType.All)]
    public async Task A_policy_for_writes_is_refused_because_no_write_reads_one(PolicyType policyType)
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_write_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var act = () => _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "writes",
            TableName = tableName,
            PolicyType = policyType,
            Expression = "owner_id IS NOT NULL"
        });

        (await act.Should().ThrowAsync<ValidationException>()).Which.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    /// <summary>
    /// A policy that cannot hold as a predicate over its table used to be stored, and then failed
    /// every read of the table with a 500. It is the caller's mistake, reported once, at registration.
    /// </summary>
    [Theory]
    [InlineData("no_such_column = 'x'", "a name the table does not have")]
    [InlineData("owner_id = current_user_id()", "a function that does not exist")]
    [InlineData("owner_id > now()", "a comparison PostgreSQL cannot type")]
    public async Task An_expression_that_is_not_a_predicate_over_the_table_is_refused_at_registration(string expression, string why)
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_verify_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        var act = () => _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "broken",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = expression
        });
        (await act.Should().ThrowAsync<SchemaException>(why)).Which.ErrorCode.Should().Be("INVALID_EXPRESSION");

        // A quoted column name reads like a bare one, and a literal is never mistaken for a column.
        var ok = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "fine",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "\"owner_id\" IS NOT NULL OR owner_id = 'no_such_column'"
        }, TestContext.Current.CancellationToken);

        var update = () => _policies.UpdatePolicyAsync(projectId, ok.Id, new UpdatePolicyRequest { Expression = expression });
        (await update.Should().ThrowAsync<SchemaException>("an update is verified the same way"))
            .Which.ErrorCode.Should().Be("INVALID_EXPRESSION");
    }

    private Task<TableMetadata> CreateTableAsync(Guid projectId, string tableName) =>
        _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns = [new CreateColumnRequest { LogicalName = "owner_id", DataType = MorphDataType.Text }]
        });
}
