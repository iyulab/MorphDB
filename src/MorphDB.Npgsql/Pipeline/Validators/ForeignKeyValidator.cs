using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates foreign key references at application layer (Virtual FK).
/// Checks that referenced records exist in target tables.
/// </summary>
public sealed class ForeignKeyValidator : IValidator
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;

    public ForeignKeyValidator(NpgsqlDataSource dataSource, IMetadataRepository metadataRepository)
    {
        _dataSource = dataSource;
        _metadataRepository = metadataRepository;
    }

    public int Order => PipelineOrder.ForeignKeyValidator;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ValidateForeignKeys
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public async Task ExecuteAsync(IWriteContext context)
    {
        // Get relations where this table is the source (has FK column)
        var relations = context.Table.Relations
            .Where(r => r.IsActive && r.EnforceOnWrite && r.SourceTableId == context.Table.TableId)
            .ToList();

        if (relations.Count == 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(context.CancellationToken);

        foreach (var relation in relations)
        {
            // Find the source column in this table
            var sourceColumn = context.Table.Columns
                .FirstOrDefault(c => c.ColumnId == relation.SourceColumnId);

            if (sourceColumn is null) continue;

            // Skip if the FK field is not in the data or is null
            if (!context.Data.TryGetValue(sourceColumn.LogicalName, out var value) || value is null)
            {
                continue;
            }

            // Get target table metadata
            var targetTable = await _metadataRepository.GetTableByIdAsync(
                relation.TargetTableId,
                includeColumns: true,
                context.CancellationToken);

            if (targetTable is null) continue;

            // Find target column
            var targetColumn = targetTable.Columns
                .FirstOrDefault(c => c.ColumnId == relation.TargetColumnId);

            if (targetColumn is null) continue;

            // Check if referenced record exists
            var exists = await CheckReferenceExistsAsync(
                connection,
                targetTable,
                targetColumn,
                value,
                context.CancellationToken);

            if (!exists)
            {
                ((WriteContext)context).AddError(
                    sourceColumn.LogicalName,
                    ValidationErrorCodes.ForeignKeyViolation,
                    $"Referenced record with {targetColumn.LogicalName}='{value}' does not exist in table '{targetTable.LogicalName}'.",
                    value);
            }
        }
    }

    private static async Task<bool> CheckReferenceExistsAsync(
        NpgsqlConnection connection,
        TableMetadata targetTable,
        ColumnMetadata targetColumn,
        object value,
        CancellationToken cancellationToken)
    {
        var sql = $"SELECT EXISTS(SELECT 1 FROM {targetTable.PhysicalName} WHERE {targetColumn.PhysicalName} = @value";

        // For soft-delete enabled tables, exclude deleted records
        if (targetTable.SoftDeleteEnabled)
        {
            sql += " AND _deleted_at IS NULL";
        }

        sql += ")";

        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { value }, cancellationToken: cancellationToken));
    }
}
