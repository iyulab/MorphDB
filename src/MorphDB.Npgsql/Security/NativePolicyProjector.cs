using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Security;

/// <summary>
/// Projects the policies stored in <c>morphdb._morph_security_policies</c> onto PostgreSQL's own
/// row-level security, so the database refuses a hidden row to every statement — and a write a
/// policy does not allow — whichever component built the SQL. The stored rows stay the source of
/// truth; the native policies are derived from them and can always be rebuilt.
/// </summary>
/// <remarks>
/// <para>A table with at least one active policy gets row-level security enabled and forced (the
/// service role owns its tables, and an owner is exempt unless forced), one permissive policy that
/// admits everything, and one restrictive policy per stored policy. Stored policies therefore
/// combine with <c>AND</c>, as they always have.</para>
/// <para>The restrictive policies are for the role MorphDB logs in as, and only for it. A caller
/// that bypasses row-level security reaches the table as <see cref="MorphConnectionFactory.BypassRole"/>,
/// which no restrictive policy names and the permissive one admits — so the expression itself carries
/// no bypass branch, and PostgreSQL can answer it from an index. A connection that never went through
/// the factory is the login role with no context, and sees nothing a policy guards.</para>
/// <para>A stored policy's type is the command its native policy is for, and decides which rows its
/// expression is asked about: the rows a statement finds (<c>USING</c> — a row that fails is not
/// there for the statement), the rows it would leave (<c>WITH CHECK</c> — a row that fails refuses
/// the statement), or both. See <see cref="NativeFor"/>.</para>
/// <para>A placeholder is read from the session settings <see cref="MorphConnectionFactory"/> writes
/// on every connection. A setting is text, and a policy compares it with columns of any type, so each
/// placeholder occurrence is cast to the type PostgreSQL itself infers for a parameter in that
/// position — the same inference that typed the quoted literal a placeholder used to become.</para>
/// </remarks>
internal sealed partial class NativePolicyProjector
{
    /// <summary>The permissive policy every restricted table carries; the restrictive ones narrow it.</summary>
    internal const string AllowPolicyName = "morphdb_allow";

    /// <summary>What the name of every native policy projected from a stored one starts with.</summary>
    internal const string PolicyNamePrefix = "morphdb_p_";

    // What a stored policy that cannot be translated is projected as: no row, for the role the
    // policies restrict. A caller that bypasses row-level security still reaches the table.
    private const string NoRow = "false";

    private const string IndeterminateDatatype = "42P18";

    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILogger _logger;

    public NativePolicyProjector(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository,
        ILogger logger)
    {
        _connections = connections;
        _metadataRepository = metadataRepository;
        _logger = logger;
    }

    /// <summary>A stored policy, as much of it as the projection reads.</summary>
    internal sealed record StoredPolicy(Guid Id, string Name, PolicyType PolicyType, string Expression, bool IsActive);

    /// <summary>
    /// A native policy a table is expected to carry: the command it is for, and the clauses
    /// PostgreSQL accepts for that command — <c>null</c> for one it does not take.
    /// </summary>
    internal sealed record NativePolicy(string Name, string Command, string Role, string? Using, string? Check);

    /// <summary>
    /// Brings the native policies of every table that has a stored policy row in line with those
    /// rows. Run at start-up, so a database whose policies were stored by a version that did not
    /// project them is enforced by PostgreSQL from the first request.
    /// </summary>
    public async Task ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> tableIds;
        await using (var connection = await _connections.OpenSystemAsync(cancellationToken))
        {
            // A deleted table keeps its policy rows; its physical table, and every native policy on
            // it, went with the DROP.
            tableIds = (await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT DISTINCT p.table_id
                FROM morphdb._morph_security_policies p
                INNER JOIN morphdb._morph_tables t ON t.table_id = p.table_id AND t.is_active = true
                """,
                cancellationToken: cancellationToken))).AsList();
        }

        foreach (var tableId in tableIds)
        {
            var table = await _metadataRepository.GetTableByIdAsync(tableId, includeColumns: true, cancellationToken);
            if (table is not null)
            {
                await ReconcileAsync(table, cancellationToken);
            }
        }

        await ReportMaterializedViewsOverPoliciesAsync(cancellationToken);
    }

    /// <summary>
    /// A materialized view over a table with a policy can no longer be created, but an earlier
    /// version allowed it, and the copy such a view holds was filled past the policy. Nothing is
    /// dropped here — the view and the policy are both the operator's to keep or let go — but each
    /// pair is named in the log, and the view cannot be read or refreshed while the pair exists.
    /// </summary>
    private async Task ReportMaterializedViewsOverPoliciesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        var conflicts = await MaterializedViewPolicyGuard.FindAsync(
            connection, transaction: null, viewPhysicalName: null, tableId: null, cancellationToken);

        foreach (var conflict in conflicts)
        {
            LogMaterializedViewOverPolicy(_logger, conflict.ViewName, conflict.TableName, conflict.PolicyName);
        }
    }

    /// <summary>
    /// Makes the table's native policies match its stored policy rows. A stored policy that cannot
    /// be translated is replaced by one that denies every row, so the table is never left open.
    /// Returns the number of DDL statements issued — zero when nothing had to change.
    /// </summary>
    public async Task<int> ReconcileAsync(TableMetadata table, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        await using var tableLock = await LockAsync(connection, table, cancellationToken);

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@Table) IS NOT NULL",
            new { Table = DdlBuilder.QuoteIdentifier(table.PhysicalName) },
            cancellationToken: cancellationToken));
        if (!exists)
        {
            // Metadata names a table PostgreSQL does not have. There is nothing to protect, and
            // nothing to attach a policy to.
            LogMissingPhysicalTable(_logger, table.LogicalName);
            return 0;
        }

        var stored = await LoadStoredAsync(connection, table.TableId, cancellationToken);
        var plan = await PlanAsync(connection, table, stored, strictPolicyId: null, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var statements = await ApplyAsync(connection, transaction, table, plan, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return statements;
    }

    /// <summary>
    /// Serializes everything that reads a table's stored policies and then changes its native ones.
    /// Held on the session, not a transaction: the translation that happens between the read and
    /// the DDL must run outside a transaction (see <see cref="TranslateAsync"/>).
    /// </summary>
    public static async Task<IAsyncDisposable> LockAsync(
        NpgsqlConnection connection,
        TableMetadata table,
        CancellationToken cancellationToken)
    {
        var key = "morphdb.rls:" + table.TableId.ToString("N");
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_lock(hashtextextended(@Key, 0))",
            new { Key = key },
            cancellationToken: cancellationToken));
        return new TableLock(connection, key);
    }

    /// <summary>The table's stored policies, in the order they were declared.</summary>
    public static async Task<IReadOnlyList<StoredPolicy>> LoadStoredAsync(
        NpgsqlConnection connection,
        Guid tableId,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        var rows = await connection.QueryAsync<StoredPolicyRow>(new CommandDefinition(
            """
            SELECT id, name, policy_type, expression, is_active
            FROM morphdb._morph_security_policies
            WHERE table_id = @TableId
            ORDER BY ordinal_position, id
            """,
            new { TableId = tableId },
            transaction,
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new StoredPolicy(r.Id, r.Name, (PolicyType)r.PolicyType, r.Expression, r.IsActive))
            .ToList();
    }

    /// <summary>
    /// Follows a column's change of logical name in every stored policy of its table, inside the
    /// caller's transaction — so the rename and the expressions that name the column are written
    /// together or not at all. A policy is written in logical names: one left naming the old name
    /// could not be translated the next time the table's policies are reconciled, and the table
    /// would be closed to every caller.
    /// </summary>
    /// <remarks>
    /// <para>Only the stored text changes. The native policy needs nothing: PostgreSQL holds the
    /// column itself, not its name, and follows it through a rename.</para>
    /// <para>The name is replaced where it is an identifier — bare or double-quoted — and nowhere
    /// else: not inside a string literal, not inside a longer name, and not inside a placeholder
    /// (<c>{{email}}</c> is the caller's e-mail address whatever the column is called).</para>
    /// <para>The caller holds <see cref="LockAsync"/>.</para>
    /// </remarks>
    /// <exception cref="SchemaException">
    /// <c>COLUMN_USED_BY_POLICY</c>: a policy already uses the new name for something that is not a
    /// column — a function or a keyword. Once a column carried that name the policy would read it
    /// as the column, and mean something else or nothing at all.
    /// </exception>
    public static async Task RenameColumnInStoredAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TableMetadata table,
        string oldLogicalName,
        string newLogicalName,
        CancellationToken cancellationToken)
    {
        if (oldLogicalName == newLogicalName)
        {
            return;
        }

        var stored = await LoadStoredAsync(connection, table.TableId, cancellationToken, transaction);

        var alreadyUsing = stored
            .Where(p => SqlIdentifierRewriter.Identifiers(p.Expression).Contains(newLogicalName, StringComparer.Ordinal))
            .Select(p => $"'{p.Name}'")
            .ToList();
        if (alreadyUsing.Count > 0)
        {
            throw new SchemaException(
                "COLUMN_USED_BY_POLICY",
                $"Column '{oldLogicalName}' of table '{table.LogicalName}' cannot be renamed to '{newLogicalName}': " +
                $"row-level security {(alreadyUsing.Count == 1 ? "policy" : "policies")} {string.Join(", ", alreadyUsing)} " +
                $"already {(alreadyUsing.Count == 1 ? "uses" : "use")} that name for something that is not a column. " +
                "Change the policy first, or choose another name.");
        }

        var written = BareIdentifier().IsMatch(newLogicalName) ? newLogicalName : DdlBuilder.QuoteIdentifier(newLogicalName);

        foreach (var policy in stored)
        {
            var expression = SqlIdentifierRewriter.Rewrite(
                policy.Expression, token => token == oldLogicalName ? written : null);
            if (expression == policy.Expression)
            {
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE morphdb._morph_security_policies
                SET expression = @Expression, updated_at = NOW()
                WHERE id = @Id
                """,
                new { policy.Id, Expression = expression },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    /// <summary>
    /// Works out the native policies <paramref name="policies"/> call for: every active policy is
    /// projected, whatever its type.
    /// </summary>
    /// <remarks>
    /// <para><paramref name="strictPolicyId"/> is the policy a caller is writing right now. Its
    /// expression is verified whether or not it is projected, and a failure is the caller's to hear
    /// about (<c>INVALID_EXPRESSION</c>). Any other policy that fails was stored earlier and cannot
    /// be refused any more, so it is projected as a policy that denies every row, and logged.</para>
    /// <para><paramref name="connection"/> must not be in a transaction.</para>
    /// </remarks>
    public async Task<IReadOnlyList<NativePolicy>> PlanAsync(
        NpgsqlConnection connection,
        TableMetadata table,
        IReadOnlyList<StoredPolicy> policies,
        Guid? strictPolicyId,
        CancellationToken cancellationToken)
    {
        var plan = new List<NativePolicy>();
        string? loginRole = null;

        foreach (var policy in policies)
        {
            var projected = policy.IsActive;
            var strict = policy.Id == strictPolicyId;
            if (!projected && !strict)
            {
                continue;
            }

            string expression;
            try
            {
                expression = await TranslateAsync(connection, policy.Expression, table, cancellationToken);
            }
            catch (SchemaException ex) when (!strict)
            {
                LogUntranslatablePolicy(_logger, table.LogicalName, policy.Name, ex.Message);
                expression = NoRow;
            }

            if (projected)
            {
                loginRole ??= await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT session_user::text", cancellationToken: cancellationToken))
                    ?? throw new InvalidOperationException("The connection reports no session user.");
                plan.AddRange(NativeFor(table, policy, expression, loginRole));
            }
        }

        return plan;
    }

    /// <summary>
    /// The native policies one stored policy stands for. <paramref name="expression"/> is its
    /// translated predicate; <paramref name="role"/> is the login role they restrict.
    /// </summary>
    /// <remarks>
    /// <para>PostgreSQL takes <c>USING</c> for the rows a command finds and <c>WITH CHECK</c> for
    /// the rows it leaves, and not every command has both: an <c>INSERT</c> finds no row, and a
    /// <c>SELECT</c> or <c>DELETE</c> leaves none. An <c>Update</c> policy is asked both ways — a row
    /// it does not admit cannot be updated, and a row cannot be updated into one it does not
    /// admit.</para>
    /// <para>A table with soft delete does not delete a record by id: it marks the row, with an
    /// <c>UPDATE</c>, which a policy for <c>DELETE</c> never sees. So on such a table a
    /// <c>Delete</c> policy also refuses an update that leaves a row marked deleted unless the
    /// policy admits that row. PostgreSQL cannot be asked about the row as it was before an update
    /// and as it is after in one expression, so this is a check on the row the update leaves — the
    /// statement is refused, where a <c>DELETE</c> would have found no row.</para>
    /// </remarks>
    private static IEnumerable<NativePolicy> NativeFor(TableMetadata table, StoredPolicy policy, string expression, string role)
    {
        yield return policy.PolicyType switch
        {
            PolicyType.Select => Native(policy.Id, "SELECT", role, expression, check: null),
            PolicyType.Insert => Native(policy.Id, "INSERT", role, usingExpression: null, expression),
            PolicyType.Update => Native(policy.Id, "UPDATE", role, expression, expression),
            PolicyType.Delete => Native(policy.Id, "DELETE", role, expression, check: null),

            // All — and a type this version does not know, which restricts every command rather
            // than none.
            _ => Native(policy.Id, "ALL", role, expression, expression),
        };

        if (policy.PolicyType == PolicyType.Delete && table.SoftDeleteEnabled)
        {
            var deletedAt = DdlBuilder.QuoteIdentifier(SystemColumns.DeletedAt);
            yield return Native(policy.Id, "UPDATE", role, "true", $"{deletedAt} IS NULL OR ({expression})");
        }
    }

    private static NativePolicy Native(Guid policyId, string command, string role, string? usingExpression, string? check) =>
        new(NameFor(policyId, command, role, usingExpression, check), command, role, usingExpression, check);

    /// <summary>
    /// Issues the DDL that takes the table from the native policies it has to the ones in
    /// <paramref name="plan"/>, inside the caller's transaction — so a stored change and its
    /// projection commit or fail together. Returns the number of statements issued.
    /// </summary>
    public async Task<int> ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TableMetadata table,
        IReadOnlyList<NativePolicy> plan,
        CancellationToken cancellationToken)
    {
        var tableName = DdlBuilder.QuoteIdentifier(table.PhysicalName);

        var state = await connection.QuerySingleOrDefaultAsync<TableSecurityRow>(new CommandDefinition(
            """
            SELECT relrowsecurity AS enabled, relforcerowsecurity AS forced
            FROM pg_class
            WHERE oid = to_regclass(@Table)
            """,
            new { Table = tableName },
            transaction,
            cancellationToken: cancellationToken));

        if (state is null)
        {
            LogMissingPhysicalTable(_logger, table.LogicalName);
            return 0;
        }

        var existing = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT polname::text FROM pg_policy WHERE polrelid = to_regclass(@Table)",
            new { Table = tableName },
            transaction,
            cancellationToken: cancellationToken))).ToHashSet(StringComparer.Ordinal);

        var statements = new List<string>();

        if (plan.Count > 0)
        {
            if (!state.Enabled)
            {
                statements.Add($"ALTER TABLE {tableName} ENABLE ROW LEVEL SECURITY");
            }

            if (!state.Forced)
            {
                statements.Add($"ALTER TABLE {tableName} FORCE ROW LEVEL SECURITY");
            }

            if (!existing.Contains(AllowPolicyName))
            {
                statements.Add(
                    $"CREATE POLICY {AllowPolicyName} ON {tableName} AS PERMISSIVE FOR ALL USING (true) WITH CHECK (true)");
            }

            foreach (var policy in plan.Where(p => !existing.Contains(p.Name)).DistinctBy(p => p.Name))
            {
                var clauses =
                    (policy.Using is null ? "" : $" USING ({policy.Using})") +
                    (policy.Check is null ? "" : $" WITH CHECK ({policy.Check})");
                statements.Add(
                    $"CREATE POLICY {DdlBuilder.QuoteIdentifier(policy.Name)} ON {tableName} " +
                    $"AS RESTRICTIVE FOR {policy.Command} TO {DdlBuilder.QuoteIdentifier(policy.Role)}{clauses}");
            }

            var expected = plan.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in existing.Where(n => n.StartsWith(PolicyNamePrefix, StringComparison.Ordinal) && !expected.Contains(n)))
            {
                statements.Add($"DROP POLICY {DdlBuilder.QuoteIdentifier(stale)} ON {tableName}");
            }
        }
        else
        {
            foreach (var managed in existing.Where(IsManaged))
            {
                statements.Add($"DROP POLICY {DdlBuilder.QuoteIdentifier(managed)} ON {tableName}");
            }

            // A policy someone else attached to the table is theirs, and switching row-level
            // security off would switch it off with ours.
            if (existing.All(IsManaged))
            {
                if (state.Forced)
                {
                    statements.Add($"ALTER TABLE {tableName} NO FORCE ROW LEVEL SECURITY");
                }

                if (state.Enabled)
                {
                    statements.Add($"ALTER TABLE {tableName} DISABLE ROW LEVEL SECURITY");
                }
            }
        }

        foreach (var statement in statements)
        {
            await connection.ExecuteAsync(new CommandDefinition(statement, transaction: transaction, cancellationToken: cancellationToken));
        }

        return statements.Count;
    }

    /// <summary>
    /// Translates a policy expression — logical column names, <c>{{placeholders}}</c> — into the
    /// predicate PostgreSQL evaluates: physical column names, and each placeholder read from the
    /// session and cast to the type its position calls for.
    /// </summary>
    /// <remarks>
    /// <para>The type is PostgreSQL's own answer. Each placeholder occurrence is prepared as a
    /// parameter of its own and the inferred parameter types are read back: <c>owner_id = $1</c>
    /// says <c>uuid</c> for a uuid column and <c>text</c> for a text one, exactly as the untyped
    /// literal a placeholder used to become was typed. Where nothing fixes the type
    /// (<c>$1 IS NOT NULL</c>) PostgreSQL says so, and the parameter is text — what the literal
    /// would have been.</para>
    /// <para>Preparing the statement is also the proof that the expression is a predicate over this
    /// table: a name the table does not have, a function that does not exist, or a comparison of
    /// the wrong type is refused here, once, instead of failing every statement that reads the
    /// table afterwards.</para>
    /// <para><paramref name="connection"/> must not be in a transaction — a refused
    /// <c>PREPARE</c> would abort it, and the retry that types an undetermined parameter with it.</para>
    /// </remarks>
    /// <exception cref="SchemaException"><c>INVALID_EXPRESSION</c>: the expression is not a predicate over the table.</exception>
    public static async Task<string> TranslateAsync(
        NpgsqlConnection connection,
        string expression,
        TableMetadata table,
        CancellationToken cancellationToken)
    {
        InlineExpressionValidator.Validate(expression, "Policy");

        var (parameterized, slots) = Parameterize(expression, table);
        var template = ToPhysical(parameterized, table, reportedAs: expression);

        var types = await InferSlotTypesAsync(connection, expression, template, slots, table, cancellationToken);
        return RenderSlots(template, index => slots[index].ToNative(types[index]));
    }

    /// <summary>
    /// Rewrites the column names a policy is written in — the table's logical names, as the caller
    /// declared them — to the physical names PostgreSQL knows the columns by.
    /// <paramref name="reportedAs"/> is the text to quote in a refusal: <paramref name="expression"/>
    /// is no longer what the caller wrote once its placeholders have been replaced.
    /// </summary>
    private static string ToPhysical(string expression, TableMetadata table, string reportedAs) =>
        SqlIdentifierRewriter.Rewrite(expression, token =>
        {
            var column = table.Columns.FirstOrDefault(c => c.LogicalName == token);
            if (column is not null)
            {
                if (column.IsDerived)
                {
                    throw new SchemaException(
                        "INVALID_EXPRESSION",
                        $"Policy expression '{reportedAs}' names '{token}', a derived column; a policy reads only stored columns.");
                }

                return DdlBuilder.QuoteIdentifier(column.PhysicalName);
            }

            return SystemColumns.IsSystemColumn(token) ? DdlBuilder.QuoteIdentifier(token) : null;
        });

    /// <summary>
    /// Replaces every placeholder with a positional parameter of its own. A placeholder is only a
    /// placeholder outside quotes: inside a string literal it would be text, not the caller's value,
    /// and a policy that compares with the text <c>{{user_id}}</c> is a mistake worth refusing.
    /// </summary>
    private static (string Parameterized, List<Slot> Slots) Parameterize(string expression, TableMetadata table)
    {
        var result = new StringBuilder(expression.Length);
        var slots = new List<Slot>();

        for (var i = 0; i < expression.Length;)
        {
            var c = expression[i];

            if (c is '\'' or '"')
            {
                // The validator has already refused an unterminated quote. A doubled quote ends one
                // quoted run and starts the next, which reads the same for this purpose.
                var end = expression.IndexOf(c, i + 1);
                var quoted = expression.AsSpan(i, end - i + 1);
                if (Placeholder().IsMatch(quoted))
                {
                    throw NotAPredicate(expression, table, "a placeholder inside quotes is text, not the caller's value; write it unquoted");
                }

                result.Append(quoted);
                i = end + 1;
                continue;
            }

            if (c == '$' && i + 1 < expression.Length && char.IsAsciiDigit(expression[i + 1]))
            {
                throw NotAPredicate(expression, table, "a numbered parameter ($1, $2, …) has no value in a policy");
            }

            var match = c == '{' ? PlaceholderAt().Match(expression, i) : Match.Empty;
            if (match.Success)
            {
                slots.Add(Slot.For(match.Groups[1].Value.ToLowerInvariant())
                    ?? throw NotAPredicate(expression, table, $"'{match.Value}' is not a placeholder"));
                result.Append('$').Append(slots.Count.ToString(CultureInfo.InvariantCulture));
                i += match.Length;
                continue;
            }

            result.Append(c);
            i++;
        }

        return (result.ToString(), slots);
    }

    private static async Task<string[]> InferSlotTypesAsync(
        NpgsqlConnection connection,
        string expression,
        string template,
        List<Slot> slots,
        TableMetadata table,
        CancellationToken cancellationToken)
    {
        var statementName = "morphdb_policy_probe_" + Guid.NewGuid().ToString("N");
        var forcedToText = new bool[slots.Count];

        while (true)
        {
            var probe = RenderSlots(template, index =>
            {
                var parameter = "$" + (index + 1).ToString(CultureInfo.InvariantCulture);
                return slots[index].FixedType is { } fixedType ? $"{parameter}::{fixedType}"
                    : forcedToText[index] ? $"{parameter}::text"
                    : parameter;
            });

            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"PREPARE {statementName} AS SELECT 1 FROM {DdlBuilder.QuoteIdentifier(table.PhysicalName)} WHERE ({probe})",
                    cancellationToken: cancellationToken));
            }
            catch (PostgresException ex) when (ex.SqlState == IndeterminateDatatype && TryMarkUndetermined(ex, forcedToText))
            {
                continue;
            }
            catch (PostgresException ex)
            {
                throw NotAPredicate(expression, table, ex.MessageText);
            }

            string[] types;
            try
            {
                types = await connection.QuerySingleAsync<string[]>(new CommandDefinition(
                    "SELECT parameter_types::text[] FROM pg_prepared_statements WHERE name = @Name",
                    new { Name = statementName },
                    cancellationToken: cancellationToken));
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition($"DEALLOCATE {statementName}", cancellationToken: CancellationToken.None));
            }

            if (types.Length != slots.Count)
            {
                // A placeholder PostgreSQL did not read as a value — run into the name beside it, say.
                throw NotAPredicate(expression, table, "a placeholder must stand where a value can");
            }

            return types;
        }
    }

    /// <summary>
    /// PostgreSQL names the parameter it could not type (<c>$2</c>). True when that parameter can
    /// still be given a type — it is then marked, and the statement is prepared again.
    /// </summary>
    private static bool TryMarkUndetermined(PostgresException exception, bool[] forcedToText)
    {
        var match = ParameterReference().Match(exception.MessageText);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 1
            || number > forcedToText.Length
            || forcedToText[number - 1])
        {
            return false;
        }

        forcedToText[number - 1] = true;
        return true;
    }

    /// <summary>
    /// Rewrites each <c>$n</c> outside quotes with what <paramref name="slot"/> returns for the
    /// zero-based slot <c>n - 1</c>. A <c>$5</c> inside a string literal is part of the caller's text.
    /// </summary>
    private static string RenderSlots(string template, Func<int, string> slot)
    {
        var result = new StringBuilder(template.Length);

        for (var i = 0; i < template.Length;)
        {
            var c = template[i];

            if (c is '\'' or '"')
            {
                var end = template.IndexOf(c, i + 1);
                result.Append(template, i, end - i + 1);
                i = end + 1;
                continue;
            }

            if (c == '$' && i + 1 < template.Length && char.IsAsciiDigit(template[i + 1]))
            {
                var end = i + 1;
                while (end < template.Length && char.IsAsciiDigit(template[end]))
                {
                    end++;
                }

                var number = int.Parse(template.AsSpan(i + 1, end - i - 1), NumberStyles.None, CultureInfo.InvariantCulture);
                result.Append(slot(number - 1));
                i = end;
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Deterministic, and different whenever what PostgreSQL is asked to enforce differs — the
    /// command, the role it restricts, either clause, or which clauses there are — so a changed
    /// policy is a policy to create and a policy to drop, and an unchanged one is neither. A
    /// deployment that moves to another login role has every policy rebuilt for it at start-up.
    /// </summary>
    private static string NameFor(Guid policyId, string command, string role, string? usingExpression, string? check)
    {
        var enforced = command + "\nTO " + role + "\nUSING " + usingExpression + "\nWITH CHECK " + check;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(enforced));
        return $"{PolicyNamePrefix}{policyId:N}_{Convert.ToHexStringLower(hash, 0, 4)}";
    }

    private static bool IsManaged(string policyName) =>
        policyName == AllowPolicyName || policyName.StartsWith(PolicyNamePrefix, StringComparison.Ordinal);

    private static SchemaException NotAPredicate(string expression, TableMetadata table, string reason) =>
        new(
            "INVALID_EXPRESSION",
            $"Policy expression '{expression}' is not a predicate over table '{table.LogicalName}': {reason}");

    /// <summary>One occurrence of a placeholder, and where its value comes from.</summary>
    private sealed record Slot(string Setting, string? Claim, string? FixedType)
    {
        public static Slot? For(string placeholder) => placeholder switch
        {
            "user_id" => new Slot(MorphConnectionFactory.UserIdSetting, null, null),
            "email" => new Slot(MorphConnectionFactory.EmailSetting, null, null),
            "role" => new Slot(MorphConnectionFactory.RoleSetting, null, null),
            "project_id" => new Slot(MorphConnectionFactory.ProjectIdSetting, null, null),
            "is_authenticated" => new Slot(MorphConnectionFactory.IsAuthenticatedSetting, null, "boolean"),
            _ when placeholder.StartsWith("claims.", StringComparison.Ordinal)
                => new Slot(MorphConnectionFactory.ClaimsSetting, placeholder[7..], null),
            _ => null,
        };

        /// <summary>
        /// An absent value is an empty setting, and reads as <c>NULL</c> — so a comparison with it
        /// is not true and the row stays hidden, as it did when the placeholder became <c>NULL</c>.
        /// </summary>
        public string ToNative(string type)
        {
            if (FixedType is not null)
            {
                return $"(current_setting('{Setting}', true) = 'true')";
            }

            var value = $"nullif(current_setting('{Setting}', true), '')";
            return Claim is null
                ? $"({value}::{type})"
                : $"(({value}::jsonb ->> '{Claim.Replace("'", "''", StringComparison.Ordinal)}')::{type})";
        }
    }

    private sealed class TableLock(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await connection.ExecuteAsync("SELECT pg_advisory_unlock(hashtextextended(@Key, 0))", new { Key = key });
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or ObjectDisposedException)
            {
                // The lock belongs to the session: a connection that can no longer run a statement
                // has given it up, or gives it up when it is reset on its way back to the pool.
            }
        }
    }

    /// <summary>Settable properties, not a positional record — see <c>SecurityPolicyService.PolicyRecord</c>.</summary>
    private sealed class StoredPolicyRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int PolicyType { get; set; }
        public string Expression { get; set; } = "";
        public bool IsActive { get; set; }
    }

    private sealed class TableSecurityRow
    {
        public bool Enabled { get; set; }
        public bool Forced { get; set; }
    }

    [GeneratedRegex(@"\{\{(\w+(?:\.\w+)?)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\G\{\{(\w+(?:\.\w+)?)\}\}")]
    private static partial Regex PlaceholderAt();

    [GeneratedRegex(@"\$(\d+)")]
    private static partial Regex ParameterReference();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex BareIdentifier();

    [LoggerMessage(
        LogLevel.Warning,
        "Row-level security policy '{Policy}' on table '{Table}' cannot be enforced as written, so it admits no row: what " +
        "the policy applies to is refused to every connection that does not bypass row-level security, until the policy " +
        "is corrected or removed: {Reason}")]
    private static partial void LogUntranslatablePolicy(ILogger logger, string table, string policy, string reason);

    [LoggerMessage(
        LogLevel.Warning,
        "Materialized view '{View}' reads table '{Table}', which has row-level security policy '{Policy}'. A materialized " +
        "view is a stored copy that no policy filters, so reading or refreshing it is refused until the view is deleted " +
        "(a plain view can take its place) or the policy is deleted or deactivated.")]
    private static partial void LogMaterializedViewOverPolicy(ILogger logger, string view, string table, string policy);

    [LoggerMessage(
        LogLevel.Warning,
        "Table '{Table}' has stored row-level security policies but no physical table; nothing was projected.")]
    private static partial void LogMissingPhysicalTable(ILogger logger, string table);
}
