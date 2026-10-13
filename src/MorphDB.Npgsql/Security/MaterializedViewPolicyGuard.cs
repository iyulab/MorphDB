using Dapper;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// A materialized view is a stored copy of its query's result. PostgreSQL applies a table's
/// policies to the statement that fills the copy — which MorphDB runs as itself — and none to the
/// copy, so a materialized view over a table with a row-level security policy would hand every
/// caller every row. The combination is refused from both sides; this is where each side asks
/// whether it exists, and what it says when it does.
/// </summary>
/// <remarks>
/// What a view reads is asked of PostgreSQL's catalog — the relations its rewrite rule depends on,
/// followed through any view in between — never worked out from the view's SQL.
/// </remarks>
internal static class MaterializedViewPolicyGuard
{
    /// <summary>The code every refusal here carries.</summary>
    internal const string ErrorCode = "INVALID_OPERATION";

    /// <summary>
    /// Every materialized view that reads a table with an active stored policy, one row per view,
    /// table and policy — narrowed to one view (by physical name), to one table, or to both.
    /// </summary>
    public static async Task<IReadOnlyList<Conflict>> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string? viewPhysicalName,
        Guid? tableId,
        CancellationToken cancellationToken)
    {
        // From each materialized view down: a relation's rewrite rule depends on every relation its
        // query names, a plain view among them has a rule of its own, and so on to the tables. The
        // rule also depends on the view it belongs to, which is not something the view reads.
        var rows = await connection.QueryAsync<Conflict>(new CommandDefinition(
            """
            WITH RECURSIVE reads(materialized, relation) AS (
                SELECT candidate.oid, candidate.oid
                FROM pg_class candidate
                WHERE candidate.relkind = 'm'
                  AND (@View::text IS NULL OR candidate.oid = to_regclass(@View))
              UNION
                SELECT reads.materialized, dependency.refobjid
                FROM reads
                INNER JOIN pg_rewrite rule ON rule.ev_class = reads.relation
                INNER JOIN pg_depend dependency
                    ON dependency.classid = 'pg_rewrite'::regclass
                   AND dependency.objid = rule.oid
                   AND dependency.refclassid = 'pg_class'::regclass
                   AND dependency.refobjid <> reads.relation
            )
            SELECT DISTINCT
                COALESCE(declared.logical_name, materialized.relname::text) AS view_name,
                source.logical_name AS table_name,
                policy.name AS policy_name
            FROM reads
            INNER JOIN pg_class materialized ON materialized.oid = reads.materialized
            INNER JOIN pg_class relation ON relation.oid = reads.relation
            INNER JOIN morphdb._morph_tables source
                ON source.physical_name = relation.relname AND source.is_active = true
            INNER JOIN morphdb._morph_security_policies policy
                ON policy.table_id = source.table_id AND policy.is_active = true
            LEFT JOIN morphdb._morph_views declared
                ON declared.physical_name = materialized.relname AND declared.is_active = true
            WHERE (@TableId::uuid IS NULL OR source.table_id = @TableId)
            ORDER BY 1, 2, 3
            """,
            new { View = viewPhysicalName is null ? null : DdlBuilder.QuoteIdentifier(viewPhysicalName), TableId = tableId },
            transaction,
            cancellationToken: cancellationToken));

        return rows.AsList();
    }

    /// <summary>
    /// Refuses to create, refresh or read (<paramref name="action"/>) a materialized view that reads
    /// a table with a policy. Does nothing when it reads none.
    /// </summary>
    /// <exception cref="SchemaException"><c>INVALID_OPERATION</c>.</exception>
    public static async Task EnsureReadsNoTableWithPolicyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string viewLogicalName,
        string viewPhysicalName,
        string action,
        CancellationToken cancellationToken)
    {
        var conflicts = await FindAsync(connection, transaction, viewPhysicalName, tableId: null, cancellationToken);
        if (conflicts.Count == 0)
        {
            return;
        }

        var tables = conflicts
            .GroupBy(c => c.TableName, StringComparer.Ordinal)
            .Select(t => $"table '{t.Key}' ({Named("policy", "policies", t.Select(c => c.PolicyName))})");

        throw new SchemaException(
            ErrorCode,
            $"Materialized view '{viewLogicalName}' cannot be {action}: a materialized view cannot be built over a table " +
            $"with a row-level security policy, and this one reads {string.Join(", ", tables)}. A materialized view is a " +
            "stored copy that every caller would read in full, whatever the policy says. Use a plain (non-materialized) " +
            "view instead; it shows each caller only the rows that caller may read.");
    }

    /// <summary>
    /// Refuses to give <paramref name="table"/> a policy — to have one <paramref name="action"/> —
    /// while a materialized view reads the table. Asked inside the transaction that has already
    /// written the policy, so the policy itself is among what the catalog query finds.
    /// </summary>
    /// <exception cref="SchemaException"><c>INVALID_OPERATION</c>.</exception>
    public static async Task EnsureNoMaterializedViewReadsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TableMetadata table,
        string action,
        CancellationToken cancellationToken)
    {
        var conflicts = await FindAsync(connection, transaction, viewPhysicalName: null, table.TableId, cancellationToken);
        if (conflicts.Count == 0)
        {
            return;
        }

        var views = conflicts.Select(c => c.ViewName).Distinct(StringComparer.Ordinal).ToList();

        throw new SchemaException(
            ErrorCode,
            $"A row-level security policy cannot be {action} table '{table.LogicalName}': " +
            $"{Named("materialized view", "materialized views", views)} {(views.Count == 1 ? "reads" : "read")} it. " +
            "A materialized view is a stored copy that every caller would read in full, whatever the policy says. " +
            "Delete the view, or replace it with a plain (non-materialized) view, first.");
    }

    private static string Named(string one, string several, IEnumerable<string> names)
    {
        var quoted = names.Distinct(StringComparer.Ordinal).Select(n => $"'{n}'").ToList();
        return $"{(quoted.Count == 1 ? one : several)} {string.Join(", ", quoted)}";
    }

    /// <summary>Settable properties, not a positional record — see <c>SecurityPolicyService.PolicyRecord</c>.</summary>
    internal sealed class Conflict
    {
        public string ViewName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string PolicyName { get; set; } = "";
    }
}
