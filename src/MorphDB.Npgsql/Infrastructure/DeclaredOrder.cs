using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// A declared order — a lookup's choice among several matching rows, a rollup's order of collected
/// values: comma-separated <c>column [asc|desc]</c> terms over a target table's stored or system
/// columns. It is parsed, never pasted: each term reaches SQL as the target's column name and a
/// direction the server wrote.
/// </summary>
internal static class DeclaredOrder
{
    /// <summary>What is wrong with <paramref name="order"/> over <paramref name="target"/>; empty when it is a valid order.</summary>
    public static IEnumerable<string> Errors(string? order, TableMetadata target)
    {
        if (string.IsNullOrWhiteSpace(order))
            yield break;

        foreach (var term in order.Split(','))
        {
            var parts = term.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 || (parts.Length == 2 && !IsDirection(parts[1])))
            {
                yield return $"Order '{order}' is not a list of 'column [asc|desc]'.";
                yield break;
            }

            if (Column(target, parts[0]) is null)
            {
                yield return $"Order column '{parts[0]}' is not a stored column of '{target.LogicalName}'.";
            }
        }
    }

    /// <summary>
    /// The <c>ORDER BY</c> clause (with a leading space) for a valid <paramref name="order"/>, its
    /// columns qualified by <paramref name="alias"/>; empty when there is no order.
    /// </summary>
    public static string Sql(string? order, TableMetadata target, string alias)
    {
        if (string.IsNullOrWhiteSpace(order))
            return "";

        var terms = order.Split(',').Select(term =>
        {
            var parts = term.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var column = Column(target, parts[0])
                ?? throw new ArgumentException($"Order column '{parts[0]}' is not a stored column of '{target.LogicalName}'.", nameof(order));
            var descending = parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
            return $"{alias}.{DdlBuilder.QuoteIdentifier(column)}{(descending ? " DESC" : " ASC")}";
        });
        return $" ORDER BY {string.Join(", ", terms)}";
    }

    private static bool IsDirection(string word) =>
        word.Equals("asc", StringComparison.OrdinalIgnoreCase) || word.Equals("desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>The physical name of a stored or system column of <paramref name="target"/>.</summary>
    private static string? Column(TableMetadata target, string logicalName)
    {
        var column = target.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        if (column is not null)
            return column.IsDerived ? null : column.PhysicalName;

        return SystemColumns.IsSystemColumn(logicalName) ? logicalName : null;
    }
}
