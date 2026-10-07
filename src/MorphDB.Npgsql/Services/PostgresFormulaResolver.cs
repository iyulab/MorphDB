using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of formula field resolution: each formula column becomes the SQL
/// expression <see cref="FormulaSql"/> translates it to, evaluated over the row at query time.
/// </summary>
public sealed class PostgresFormulaResolver : IFormulaResolver
{
    public Task<FormulaQueryExpansion> BuildFormulaExpansionAsync(
        Guid projectId,
        Core.Models.TableMetadata sourceTable,
        IReadOnlyList<FormulaColumnInfo> formulaColumns,
        CancellationToken cancellationToken = default)
    {
        // A formula stored before declarations were checked can still be one that cannot run. It
        // fails the read that needs it, naming the column and why, rather than reading as NULL — a
        // computed value that silently is not computed is indistinguishable from a real NULL.
        var expressions = formulaColumns.ToDictionary(
            f => f.ColumnName,
            f => FormulaSql.Translate(f.Config.Formula, f.ColumnName, sourceTable.Columns),
            StringComparer.Ordinal);

        return Task.FromResult(new FormulaQueryExpansion { Expressions = expressions });
    }
}
