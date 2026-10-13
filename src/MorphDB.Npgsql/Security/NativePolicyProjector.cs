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
/// row-level security, so the database refuses a hidden row to every statement — whichever
/// component built the SQL. The stored rows stay the source of truth; the native policies are
/// derived from them and can always be rebuilt.
/// </summary>
/// <remarks>
/// <para>A table with at least one active <c>Select</c> or <c>All</c> policy gets row-level security
/// enabled and forced (the service role owns its tables, and an owner is exempt unless forced), one
/// permissive policy that admits everything, and one restrictive policy per stored policy. Stored
/// policies therefore combine with <c>AND</c>, as they always have.</para>
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

    private const string BypassIsOn = $"current_setting('{MorphConnectionFactory.BypassSetting}', true) = 'on'";

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

    /// <summary>A native policy a table is expected to carry.</summary>
    internal sealed record NativePolicy(string Name, string Command, string Using);

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
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<StoredPolicyRow>(new CommandDefinition(
            """
            SELECT id, name, policy_type, expression, is_active
            FROM morphdb._morph_security_policies
            WHERE table_id = @TableId
            ORDER BY ordinal_position, id
            """,
            new { TableId = tableId },
            cancellationToken: cancellationToken));

        return rows
            .Select(r => new StoredPolicy(r.Id, r.Name, (PolicyType)r.PolicyType, r.Expression, r.IsActive))
            .ToList();
    }

    /// <summary>
    /// Works out the native policies <paramref name="policies"/> call for. Only active
    /// <c>Select</c> and <c>All</c> policies are projected — the types a read has always applied.
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

        foreach (var policy in policies)
        {
            var projected = policy.IsActive && policy.PolicyType is PolicyType.Select or PolicyType.All;
            var strict = policy.Id == strictPolicyId;
            if (!projected && !strict)
            {
                continue;
            }

            string usingExpression;
            try
            {
                var translated = await TranslateAsync(connection, policy.Expression, table, cancellationToken);
                usingExpression = $"CASE WHEN {BypassIsOn} THEN true ELSE ({translated}) END";
            }
            catch (SchemaException ex) when (!strict)
            {
                LogUntranslatablePolicy(_logger, table.LogicalName, policy.Name, ex.Message);
                usingExpression = BypassIsOn;
            }

            if (projected)
            {
                var command = policy.PolicyType == PolicyType.All ? "ALL" : "SELECT";
                plan.Add(new NativePolicy(NameFor(policy.Id, command, usingExpression), command, usingExpression));
            }
        }

        return plan;
    }

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
                statements.Add(
                    $"CREATE POLICY {DdlBuilder.QuoteIdentifier(policy.Name)} ON {tableName} " +
                    $"AS RESTRICTIVE FOR {policy.Command} USING ({policy.Using})");
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
        var template = SecurityPolicyService.ToPhysical(parameterized, table, qualifier: null, reportedAs: expression);

        var types = await InferSlotTypesAsync(connection, expression, template, slots, table, cancellationToken);
        return RenderSlots(template, index => slots[index].ToNative(types[index]));
    }

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
    /// Deterministic, and different whenever what PostgreSQL is asked to enforce differs — so a
    /// changed expression is a policy to create and a policy to drop, and an unchanged one is
    /// neither.
    /// </summary>
    private static string NameFor(Guid policyId, string command, string usingExpression)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(command + "\n" + usingExpression));
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

    [LoggerMessage(
        LogLevel.Warning,
        "Row-level security policy '{Policy}' on table '{Table}' cannot be enforced as written, so the table is readable " +
        "only by connections that bypass row-level security until the policy is corrected or removed: {Reason}")]
    private static partial void LogUntranslatablePolicy(ILogger logger, string table, string policy, string reason);

    [LoggerMessage(
        LogLevel.Warning,
        "Table '{Table}' has stored row-level security policies but no physical table; nothing was projected.")]
    private static partial void LogMissingPhysicalTable(ILogger logger, string table);
}
