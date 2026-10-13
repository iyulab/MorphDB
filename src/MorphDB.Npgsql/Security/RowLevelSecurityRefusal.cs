using Dapper;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// What PostgreSQL's row-level security refuses, said in the caller's terms. PostgreSQL names the
/// native policy and the physical table and column; a caller knows neither, only the policy it
/// stored and the names it declared.
/// </summary>
internal static class RowLevelSecurityRefusal
{
    /// <summary>
    /// A write PostgreSQL refused with <c>42501</c>. A policy on reads also decides what a write may
    /// leave behind: a row the caller could not read back is refused rather than written and then
    /// hidden from the statement that wrote it.
    /// </summary>
    public static ForbiddenException ForWrite(TableMetadata table) =>
        new($"Row-level security on table '{table.LogicalName}' refuses this write: " +
            "it would create or change a row the caller is not allowed to read.");

    /// <summary>
    /// The refusal for a column that could not be <paramref name="change"/> because a native policy
    /// depends on it, naming the stored policies behind those native ones — or <c>null</c> when no
    /// policy depends on the column, and PostgreSQL refused for a reason of its own.
    /// </summary>
    /// <remarks>
    /// Asked of the catalog rather than read from the error: the policy's name travels in the
    /// error's detail, which the driver withholds unless a connection string opts in.
    /// </remarks>
    public static async Task<SchemaException?> ForColumnAsync(
        NpgsqlConnection connection,
        TableMetadata table,
        ColumnMetadata column,
        string change,
        CancellationToken cancellationToken)
    {
        var policies = (await connection.QueryAsync<string>(new CommandDefinition(
            $$"""
            SELECT DISTINCT COALESCE(stored.name, policy.polname::text)
            FROM pg_policy policy
            INNER JOIN pg_depend dependency
                ON dependency.classid = 'pg_policy'::regclass
               AND dependency.objid = policy.oid
               AND dependency.refclassid = 'pg_class'::regclass
               AND dependency.refobjid = policy.polrelid
            INNER JOIN pg_attribute attribute
                ON attribute.attrelid = dependency.refobjid
               AND attribute.attnum = dependency.refobjsubid
            LEFT JOIN morphdb._morph_security_policies stored
                ON replace(stored.id::text, '-', '') = substring(policy.polname::text from '^{{NativePolicyProjector.PolicyNamePrefix}}([0-9a-f]{32})_')
            WHERE policy.polrelid = to_regclass(@Table)
              AND attribute.attname = @Column
            ORDER BY 1
            """,
            new { Table = DdlBuilder.QuoteIdentifier(table.PhysicalName), Column = column.PhysicalName },
            cancellationToken: cancellationToken))).AsList();

        if (policies.Count == 0)
        {
            return null;
        }

        var named = string.Join(", ", policies.Select(p => $"'{p}'"));
        return new SchemaException(
            "COLUMN_USED_BY_POLICY",
            $"Column '{column.LogicalName}' of table '{table.LogicalName}' cannot be {change}: it is used by " +
            $"row-level security {(policies.Count == 1 ? "policy" : "policies")} {named}. " +
            "Change or delete the policy first.");
    }
}
