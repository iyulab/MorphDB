using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// The <c>EXISTS</c> subquery behind an <see cref="FilterOperator.Exists"/> or <see cref="FilterOperator.NotExists"/> condition, built the same way by
/// every reader that takes one (a row query, an aggregate): the related table under its own name, joined
/// to the filtered row by the related column, its soft-deleted rows left out as a read of it leaves them
/// out, and its conditions applied by the caller's own condition writer so an operator means one thing.
/// </summary>
internal static class RelatedRowsSql
{
    private const string Alias = "related_rows";

    /// <summary>
    /// The related tables <paramref name="values"/> name, each read once before the statement is built —
    /// building is synchronous, and a table that does not exist is the caller's error before any SQL runs.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, TableMetadata>> ResolveAsync(
        IEnumerable<object?> values,
        Func<string, CancellationToken, Task<TableMetadata>> metadataOf,
        CancellationToken cancellationToken)
    {
        var related = new Dictionary<string, TableMetadata>(StringComparer.Ordinal);
        foreach (var rows in values.OfType<RelatedRows>())
        {
            if (!related.ContainsKey(rows.Table))
            {
                related[rows.Table] = await metadataOf(rows.Table, cancellationToken);
            }
        }

        return related;
    }

    /// <summary>The subquery for <paramref name="value"/>, correlated to the filtered row's <paramref name="parentColumn"/> (a SqlKata column path).</summary>
    /// <exception cref="ValidationException">The value is not a <see cref="RelatedRows"/>, names a derived column, or nests another exists.</exception>
    public static SqlKataQuery Build(
        string column,
        object? value,
        string parentColumn,
        IReadOnlyDictionary<string, TableMetadata>? related,
        Action<SqlKataQuery, string, FilterOperator, object?> applyCondition)
    {
        if (value is not RelatedRows rows)
        {
            throw new ValidationException(column, "exists takes the related rows — a table, its column holding this row's value, and conditions.");
        }

        var table = related?.GetValueOrDefault(rows.Table)
            ?? throw new ValidationException(column, "exists narrows a read of rows; it cannot narrow a write.");

        var subquery = new SqlKataQuery($"{table.PhysicalName} as {Alias}")
            .SelectRaw("1")
            .WhereColumns(Column(table, rows.Column), "=", parentColumn);
        if (table.SoftDeleteEnabled)
        {
            subquery.WhereNull($"{Alias}.{SystemColumns.DeletedAt}");
        }

        foreach (var condition in rows.Where)
        {
            if (condition.Operator.TakesRelatedRows())
            {
                throw new ValidationException(condition.Column, "the conditions of exists are on the related row's own columns; an exists inside an exists is not taken.");
            }

            applyCondition(subquery, Column(table, condition.Column), condition.Operator, ColumnValues.OfCondition(table, condition.Column, condition.Operator, condition.Value));
        }

        return subquery;
    }

    private static string Column(TableMetadata table, string logicalName)
    {
        var column = table.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        if (column is { IsDerived: true })
        {
            throw new ValidationException(logicalName, $"'{logicalName}' of '{table.LogicalName}' is derived (a lookup, rollup or formula); exists reads stored columns.");
        }

        if (column is not null)
        {
            return $"{Alias}.{column.PhysicalName}";
        }

        return SystemColumns.IsSystemColumn(logicalName)
            ? $"{Alias}.{logicalName}"
            : throw new ColumnNotFoundException(table.LogicalName, logicalName);
    }
}
