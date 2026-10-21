using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates unique constraints at application layer.
/// Supports conditional unique (e.g., exclude soft-deleted records).
/// </summary>
public sealed class UniqueValidator : IValidator
{
    private readonly MorphConnectionFactory _connections;

    public UniqueValidator(MorphConnectionFactory connections)
    {
        _connections = connections;
    }

    public int Order => PipelineOrder.UniqueValidator;

    public bool ReadsStoredRows(IWriteContext context) =>
        UniqueColumns(context.Table).Any(c => context.Data.TryGetValue(c.LogicalName, out var value) && value is not null);

    public bool ShouldExecute(IWriteContext context)
    {
        // Upsert is excluded on purpose: a collision on the conflict key is not an error there —
        // it is the very signal that turns the write into an update. Pre-checking would veto the
        // operation's whole reason to exist; the physical ON CONFLICT resolves the key collision
        // and the executor's 23505 translation still answers for any other unique column.
        return context.Options.ValidateUnique
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update;
    }

    public async Task ExecuteAsync(IWriteContext context)
    {
        var uniqueColumns = UniqueColumns(context.Table).ToList();

        if (uniqueColumns.Count == 0)
            return;

        await using var conn = await _connections.LeaseAsync(context.CancellationToken);

        foreach (var column in uniqueColumns)
        {
            // Skip if the field is not in the data (update without changing this field)
            if (!context.Data.TryGetValue(column.LogicalName, out var value) || value is null)
            {
                continue;
            }

            // Raw JsonElement values (from System.Text.Json binding on the REST path)
            // are not bindable by Dapper — convert to the column's DB value first, and
            // reuse the converted value so the error report never leaks a raw JsonElement.
            var dbValue = TypeMapper.ToDbValue(value, column.DataType);

            // Build the unique check query
            var sql = BuildUniqueCheckQuery(context, column);
            var parameters = new DynamicParameters();
            parameters.Add("value", dbValue);

            // Exclude current record for updates
            if (context.OperationType == WriteOperationType.Update && context.RecordId.HasValue)
            {
                parameters.Add("excludeId", context.RecordId.Value);
            }

            var exists = await conn.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, parameters, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

            if (exists)
            {
                ((WriteContext)context).AddError(
                    column.LogicalName,
                    ValidationErrorCodes.UniqueViolation,
                    $"Value '{dbValue}' already exists for field '{column.LogicalName}'.",
                    dbValue);
            }
        }
    }

    private static IEnumerable<ColumnMetadata> UniqueColumns(TableMetadata table) =>
        table.Columns.Where(c => c.IsUnique && c.EnforceUniqueOnWrite && c.IsActive && !c.IsPrimaryKey);

    private static string BuildUniqueCheckQuery(IWriteContext context, ColumnMetadata column)
    {
        var tableName = context.Table.PhysicalName;
        var columnName = column.PhysicalName;

        var sql = $"SELECT EXISTS(SELECT 1 FROM {tableName} WHERE {columnName} = @value";

        // On a soft-delete table uniqueness covers the unmarked rows, as its unique index does.
        if (context.Table.SoftDeleteEnabled)
        {
            sql += " AND _deleted_at IS NULL";
        }

        // Exclude current record for updates
        if (context.OperationType == WriteOperationType.Update)
        {
            var pkColumn = context.Table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pkColumn is not null)
            {
                sql += $" AND {pkColumn.PhysicalName} != @excludeId";
            }
        }

        sql += ")";
        return sql;
    }
}
