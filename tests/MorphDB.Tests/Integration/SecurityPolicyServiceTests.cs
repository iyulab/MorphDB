using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Security;
using MorphDB.Npgsql.Services;
using MorphDB.Tests.Fixtures;
using Npgsql;

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
    private readonly SecurityPolicyService _policies;
    private readonly PostgresSchemaManager _schemaManager;
    private readonly NpgsqlDataSource _dataSource;

    public SecurityPolicyServiceTests(PostgresFixture fixture)
    {
        _dataSource = fixture.DataSource;
        _policies = new SecurityPolicyService(fixture.DataSource);
        _schemaManager = new PostgresSchemaManager(
            fixture.DataSource,
            new MetadataRepository(fixture.DataSource),
            new PostgresAdvisoryLockManager(fixture.DataSource, new AdvisoryLockOptions()),
            new Sha256NameHasher(),
            new ChangeLogger(fixture.DataSource),
            new SchemaManagerOptions());
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
            Expression = "owner_id = current_user_id()"
        });

        created.TableId.Should().NotBeEmpty("the policy must resolve the table it applies to");

        var byName = await _policies.GetPoliciesByTableNameAsync(projectId, tableName);
        byName.Should().ContainSingle(p => p.Name == "owner_reads_only");

        var byId = await _policies.GetPoliciesAsync(projectId, created.TableId);
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
        });

        await _schemaManager.DeleteTableAsync(first.TableId);
        await CreateTableAsync(projectId, tableName);

        var policies = await _policies.GetPoliciesByTableNameAsync(projectId, tableName);
        policies.Should().BeEmpty("the name now belongs to a different table");
    }

    /// <summary>
    /// A policy expression is spliced into the WHERE clause of ordinary queries, which makes it a
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
        });

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
        });

        var updated = await _policies.UpdatePolicyAsync(projectId, created.Id, new UpdatePolicyRequest
        {
            Name = "after",
            Expression = "owner_id IS NOT NULL"
        });

        updated.Name.Should().Be("after");
        (await _policies.GetPolicyAsync(projectId, created.Id))!.Expression.Should().Be("owner_id IS NOT NULL");

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
        await CreateTableAsync(projectId, tableName);

        var created = await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "temporary",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id IS NOT NULL"
        });

        await _policies.DeletePolicyAsync(projectId, created.Id);

        (await _policies.GetPolicyAsync(projectId, created.Id)).Should().BeNull();
        (await _policies.EvaluatePoliciesAsync(
            projectId, tableName, PolicyType.Select, new SecurityContext { ProjectId = projectId }))
            .Should().BeNull("no policy remains to constrain the read");
    }

    [Fact]
    public async Task Evaluation_combines_the_applicable_policies_and_substitutes_the_caller()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_eval_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "owner_only",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id = {{user_id}}"
        });
        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "not_null",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id IS NOT NULL"
        });
        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "writes_only",
            TableName = tableName,
            PolicyType = PolicyType.Insert,
            Expression = "false"
        });

        var where = await _policies.EvaluatePoliciesAsync(
            projectId,
            tableName,
            PolicyType.Select,
            new SecurityContext { ProjectId = projectId, UserId = "u-1" });

        where.Should().Contain("'u-1'", "the caller is substituted for the placeholder");
        where.Should().Contain(" AND ", "applicable policies are combined, not chosen between");
        where.Should().NotContain("false", "a policy for another operation must not apply");
    }

    /// <summary>
    /// A caller's identity is substituted into the expression as a literal. A quote inside it must
    /// stay inside it, or the caller writes the predicate rather than being filtered by it.
    /// </summary>
    [Fact]
    public async Task A_caller_identity_carrying_a_quote_cannot_break_out_of_its_literal()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_escape_" + Guid.NewGuid().ToString("N")[..8];
        await CreateTableAsync(projectId, tableName);

        await _policies.CreatePolicyAsync(projectId, new CreatePolicyRequest
        {
            Name = "owner_only",
            TableName = tableName,
            PolicyType = PolicyType.Select,
            Expression = "owner_id = {{user_id}}"
        });

        var where = await _policies.EvaluatePoliciesAsync(
            projectId,
            tableName,
            PolicyType.Select,
            new SecurityContext { ProjectId = projectId, UserId = "x' OR '1'='1" });

        where.Should().Contain("''", "the quote is doubled, not closed");
    }

    /// <summary>
    /// The write-side gate cannot reach rows that were stored before it existed — and those are the
    /// rows a deployed database actually holds. Written straight to the table here, as the released
    /// service would have accepted it.
    /// </summary>
    [Fact]
    public async Task A_hostile_expression_stored_before_the_gate_existed_fails_the_read()
    {
        var projectId = Guid.NewGuid();
        var tableName = "policy_legacy_" + Guid.NewGuid().ToString("N")[..8];
        var table = await CreateTableAsync(projectId, tableName);

        await using (var connection = await _dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO morphdb._morph_security_policies
                (id, project_id, table_id, name, description, policy_type, expression, is_active, ordinal_position, created_at, updated_at)
                VALUES (@Id, @ProjectId, @TableId, 'legacy_hostile', NULL, 0, @Expression, true, 1, NOW(), NOW())
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    ProjectId = projectId,
                    TableId = table.TableId,
                    Expression = "true) OR (1=1"
                });
        }

        var act = () => _policies.EvaluatePoliciesAsync(
            projectId, tableName, PolicyType.Select, new SecurityContext { ProjectId = projectId });

        await act.Should().ThrowAsync<SchemaException>(
            "a policy that cannot be emitted safely must fail the read, not be dropped from it");
    }

    private Task<TableMetadata> CreateTableAsync(Guid projectId, string tableName) =>
        _schemaManager.CreateTableAsync(new CreateTableRequest
        {
            ProjectId = projectId,
            LogicalName = tableName,
            Columns = [new CreateColumnRequest { LogicalName = "owner_id", DataType = MorphDataType.Text }]
        });
}
