using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates unique constraints at application layer.
/// Supports conditional unique (e.g., exclude soft-deleted records).
/// </summary>
public sealed class UniqueValidator : IValidator
{
    private readonly NpgsqlDataSource _dataSource;

    public UniqueValidator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public int Order => PipelineOrder.UniqueValidator;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ValidateUnique
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public async Task ExecuteAsync(IWriteContext context)
    {
        var uniqueColumns = context.Table.Columns
            .Where(c => c.IsUnique && c.EnforceUniqueOnWrite && c.IsActive && !c.IsPrimaryKey)
            .ToList();

        if (uniqueColumns.Count == 0)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync(context.CancellationToken);

        foreach (var column in uniqueColumns)
        {
            // Skip if the field is not in the data (update without changing this field)
            if (!context.Data.TryGetValue(column.LogicalName, out var value) || value is null)
            {
                continue;
            }

            // Build the unique check query
            var sql = BuildUniqueCheckQuery(context, column);
            var parameters = new DynamicParameters();
            parameters.Add("value", value);

            // Exclude current record for updates
            if (context.OperationType == WriteOperationType.Update && context.RecordId.HasValue)
            {
                parameters.Add("excludeId", context.RecordId.Value);
            }

            var exists = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql, parameters, cancellationToken: context.CancellationToken));

            if (exists)
            {
                ((WriteContext)context).AddError(
                    column.LogicalName,
                    ValidationErrorCodes.UniqueViolation,
                    $"Value '{value}' already exists for field '{column.LogicalName}'.",
                    value);
            }
        }
    }

    private static string BuildUniqueCheckQuery(IWriteContext context, ColumnMetadata column)
    {
        var tableName = context.Table.PhysicalName;
        var columnName = column.PhysicalName;

        var sql = $"SELECT EXISTS(SELECT 1 FROM {tableName} WHERE {columnName} = @value";

        // Add condition (e.g., exclude soft-deleted)
        if (!string.IsNullOrEmpty(column.UniqueCondition))
        {
            sql += $" AND ({column.UniqueCondition})";
        }

        // For soft-delete enabled tables, auto-exclude deleted records
        if (context.Table.SoftDeleteEnabled && string.IsNullOrEmpty(column.UniqueCondition))
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
