using MorphDB.Core.Exceptions;
using MorphDB.Core.Formula;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The one rule for turning a formula column into SQL. Reads compute a formula with it, and a
/// declaration is checked by building that same read (<see cref="Query.TableSource.VerifyAsync"/>).
/// <para>
/// A formula computes over the stored and system columns of its own row, the way a PostgreSQL
/// generated column does — not over another lookup, rollup or formula column. Its expression names
/// the table <see cref="TableAlias"/>, the name every read gives the table it computes derived
/// columns over.
/// </para>
/// </summary>
internal static class FormulaSql
{
    /// <summary>The name a formula expression calls the table whose row it computes over.</summary>
    public const string TableAlias = "base_table";

    /// <summary>
    /// Translates <paramref name="formula"/>, declared as <paramref name="columnName"/> on a table
    /// with <paramref name="columns"/>, to a SQL expression — or refuses it with
    /// <c>INVALID_EXPRESSION</c>, naming what is wrong. Nothing is ever replaced by a silent NULL.
    /// </summary>
    public static string Translate(string formula, string columnName, IEnumerable<ColumnMetadata> columns)
    {
        var parsed = new FormulaParser().Parse(formula);
        if (!parsed.IsSuccess)
        {
            throw Invalid(columnName, formula, string.Join("; ", parsed.Errors));
        }

        var all = columns.ToList();
        var derived = parsed.ColumnReferences
            .Where(name => all.Any(c => c.LogicalName == name && c.IsDerived))
            .ToList();
        if (derived.Count > 0)
        {
            throw Invalid(
                columnName,
                formula,
                $"it names {string.Join(", ", derived.Select(d => $"'{d}'"))}, a derived column; a formula computes over stored columns");
        }

        var stored = all
            .Where(c => !c.IsDerived)
            .ToDictionary(c => c.LogicalName, c => c.PhysicalName, StringComparer.Ordinal);

        var (sql, errors) = new FormulaSqlTranslator(stored).Translate(parsed.Ast!, TableAlias);
        if (errors.Count > 0)
        {
            throw Invalid(columnName, formula, string.Join("; ", errors));
        }

        return sql;
    }

    private static SchemaException Invalid(string columnName, string formula, string reason) =>
        new("INVALID_EXPRESSION", $"Formula column '{columnName}' ({formula}) cannot be computed: {reason}.");
}
