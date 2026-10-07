using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using Npgsql;
using SqlKata.Compilers;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// What a read of a table selects FROM, and how every clause of that read names a column.
/// <para>
/// A table without lookup, rollup or formula columns is read as itself. A table with them is read
/// through a derived table that computes each of them by name beside the stored columns:
/// <c>(SELECT base_table.*, &lt;lookup&gt; AS "grade", &lt;rollup&gt; AS "total", … FROM t AS base_table
/// LEFT JOIN …) AS t</c>. The statement around it then filters, orders, groups and aggregates a
/// derived column exactly as it does a stored one — a declared column is one column whichever kind
/// it is. Computing those columns only where a SELECT list happened to name them is how they came
/// to read in a list and fail everywhere else. PostgreSQL flattens the derived table, so a read
/// that does not use a derived column does not pay for it (an unused lookup join is removed).
/// </para>
/// <para>
/// The source is named what the statement calls the table — the caller's alias, else the table's
/// physical name — so a reference written as <c>{name}.{physical column}</c> means the same thing
/// with or without derived columns.
/// </para>
/// </summary>
internal sealed class TableSource
{
    private readonly TableMetadata _table;
    private readonly SqlKataQuery? _derived;
    private readonly HashSet<string> _derivedColumns;

    private TableSource(TableMetadata table, string name, SqlKataQuery? derived, HashSet<string> derivedColumns)
    {
        _table = table;
        Name = name;
        _derived = derived;
        _derivedColumns = derivedColumns;
    }

    /// <summary>What the statement calls the table (unquoted).</summary>
    public string Name { get; }

    /// <summary>Whether the table has derived columns, read through a derived table.</summary>
    public bool IsDerived => _derived is not null;

    /// <summary>Puts the source in the FROM clause of <paramref name="query"/>.</summary>
    public void ApplyFrom(SqlKataQuery query)
    {
        if (_derived is not null)
        {
            query.From(_derived, Name);
        }
        else
        {
            query.From(Name == _table.PhysicalName ? _table.PhysicalName : $"{_table.PhysicalName} as {Name}");
        }
    }

    /// <summary>
    /// The SqlKata column path (<c>name.column</c>) for a declared or system column, stored or
    /// derived. A name the table does not declare is the caller's mistake.
    /// </summary>
    public string Column(string logicalName) =>
        ColumnOrDefault(logicalName) ?? throw new ColumnNotFoundException(_table.LogicalName, logicalName);

    /// <summary>
    /// As <see cref="Column"/>, but a name the table does not declare passes through unchanged —
    /// for the clauses where it may be a SELECT alias (GROUP BY, HAVING, aggregate targets).
    /// </summary>
    public string ColumnOrSelf(string name) => ColumnOrDefault(name) ?? name;

    /// <summary><see cref="Column"/>, quoted for a raw SQL fragment.</summary>
    public string Quoted(Compiler compiler, string logicalName) => compiler.Wrap(Column(logicalName));

    private string? ColumnOrDefault(string logicalName)
    {
        if (_derivedColumns.Contains(logicalName))
        {
            return $"{Name}.{logicalName}";
        }

        var column = _table.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        if (column is not null)
        {
            if (column.IsDerived)
            {
                // Declared, but this read was given no resolver for its kind.
                throw new NotSupportedException($"Column '{logicalName}' of '{_table.LogicalName}' is derived and cannot be computed by this reader.");
            }

            return $"{Name}.{column.PhysicalName}";
        }

        return SystemColumns.IsSystemColumn(logicalName) ? $"{Name}.{logicalName}" : null;
    }

    /// <summary>
    /// Proves every lookup, rollup and formula column of <paramref name="table"/> can be computed, by
    /// building the source a read would build and asking PostgreSQL to plan reading it. The check a
    /// declaration passes is therefore the read itself — a derived column that would fail every read
    /// is refused when it is declared, with <c>INVALID_EXPRESSION</c> and the reason.
    /// </summary>
    public static async Task VerifyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        TableMetadata table,
        ILookupResolver lookupResolver,
        IRollupResolver rollupResolver,
        IFormulaResolver formulaResolver,
        CancellationToken cancellationToken)
    {
        var source = await CreateAsync(table.ProjectId, table, alias: null, lookupResolver, rollupResolver, formulaResolver, cancellationToken);
        if (!source.IsDerived)
        {
            return;
        }

        var query = new SqlKataQuery();
        source.ApplyFrom(query);
        query.Select($"{source.Name}.*");
        var compiled = new PostgresCompiler().Compile(query);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "EXPLAIN " + compiled.Sql, compiled.NamedBindings, transaction, cancellationToken: cancellationToken));
        }
        catch (PostgresException ex)
        {
            var derived = string.Join(", ", source._derivedColumns.Order(StringComparer.Ordinal).Select(c => $"'{c}'"));
            throw new SchemaException(
                "INVALID_EXPRESSION",
                $"A derived column of '{table.LogicalName}' ({derived}) cannot be computed: {ex.MessageText}.");
        }
    }

    /// <summary>
    /// The source for <paramref name="table"/>, named <paramref name="alias"/> when one is given.
    /// Each derived kind is computed when its resolver is supplied.
    /// </summary>
    public static async Task<TableSource> CreateAsync(
        Guid projectId,
        TableMetadata table,
        string? alias,
        ILookupResolver? lookupResolver,
        IRollupResolver? rollupResolver,
        IFormulaResolver? formulaResolver,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrEmpty(alias) ? table.PhysicalName : alias;

        var lookups = table.Columns
            .Where(c => c.LookupConfig is not null)
            .Select(c => new LookupColumnInfo { ColumnName = c.LogicalName, Config = c.LookupConfig!, DataType = c.DataType })
            .ToList();
        var rollups = table.Columns
            .Where(c => c.RollupConfig is not null)
            .Select(c => new RollupColumnInfo { ColumnName = c.LogicalName, Config = c.RollupConfig!, DataType = c.DataType })
            .ToList();
        var formulas = table.Columns
            .Where(c => c.FormulaConfig is not null)
            .Select(c => new FormulaColumnInfo { ColumnName = c.LogicalName, Config = c.FormulaConfig!, DataType = c.DataType })
            .ToList();

        var lookup = lookups.Count > 0 && lookupResolver is not null
            ? await lookupResolver.BuildLookupExpansionAsync(projectId, table, lookups, cancellationToken)
            : null;
        var rollup = rollups.Count > 0 && rollupResolver is not null
            ? await rollupResolver.BuildRollupExpansionAsync(projectId, table, rollups, cancellationToken)
            : null;
        var formula = formulas.Count > 0 && formulaResolver is not null
            ? await formulaResolver.BuildFormulaExpansionAsync(projectId, table, formulas, cancellationToken)
            : null;

        var expressions = new List<(string Column, string Sql)>();
        expressions.AddRange((lookup?.SelectExpressions ?? new Dictionary<string, string>()).Select(e => (e.Key, e.Value)));
        expressions.AddRange((rollup?.SubqueryExpressions ?? new Dictionary<string, string>()).Select(e => (e.Key, e.Value)));
        expressions.AddRange((formula?.Expressions ?? new Dictionary<string, string>()).Select(e => (e.Key, e.Value)));

        if (expressions.Count == 0)
        {
            return new TableSource(table, name, derived: null, []);
        }

        // Lookup joins, rollup subqueries and formula expressions all name the table base_table.
        var derived = new SqlKataQuery($"{table.PhysicalName} as {FormulaSql.TableAlias}")
            .Select($"{FormulaSql.TableAlias}.*");

        foreach (var join in lookup?.Joins ?? [])
        {
            derived.LeftJoin(
                $"{join.TargetTablePhysical} AS {join.TargetTableAlias}",
                $"{FormulaSql.TableAlias}.{join.SourceColumnPhysical}",
                $"{join.TargetTableAlias}.{join.TargetColumnPhysical}");
        }

        foreach (var (column, sql) in expressions)
        {
            derived.SelectRaw($"{sql} AS \"{column.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");
        }

        return new TableSource(table, name, derived, expressions.Select(e => e.Column).ToHashSet(StringComparer.Ordinal));
    }
}
