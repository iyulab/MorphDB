using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of lookup field resolution.
/// Generates JOINs for lookup columns during query execution.
/// </summary>
public sealed class PostgresLookupResolver : ILookupResolver
{
    private readonly IMetadataRepository _metadataRepository;
    private int _aliasCounter;

    public PostgresLookupResolver(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository;
    }

    public async Task<LookupQueryExpansion> BuildLookupExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<LookupColumnInfo> lookupColumns,
        CancellationToken cancellationToken = default)
    {
        if (lookupColumns.Count == 0)
        {
            return new LookupQueryExpansion();
        }

        var joinClauses = new List<string>();
        var joins = new List<LookupJoinInfo>();
        var selectExpressions = new Dictionary<string, string>();
        var tableAliases = new Dictionary<string, string>();

        foreach (var lookup in lookupColumns)
        {
            // Validate and get metadata
            var validation = await ValidateLookupConfigAsync(
                projectId, sourceTable, lookup.Config, cancellationToken);

            if (!validation.IsValid)
                continue;

            var targetTable = validation.TargetTable!;
            var targetColumn = validation.TargetColumn!;
            var relationColumn = validation.RelationColumn!;

            // Generate unique alias for this join
            var alias = GenerateTableAlias(targetTable.LogicalName);
            tableAliases[targetTable.LogicalName] = alias;

            // Find target PK column
            var pkColumn = targetTable.Columns
                .FirstOrDefault(c => c.IsPrimaryKey || c.LogicalName == "_id");

            if (pkColumn == null)
                continue;

            // Build LEFT JOIN clause (raw SQL format)
            var joinClause = $"LEFT JOIN {DdlBuilder.QuoteIdentifier(targetTable.PhysicalName)} AS {alias} " +
                           $"ON base_table.{DdlBuilder.QuoteIdentifier(relationColumn.PhysicalName)} = " +
                           $"{alias}.{DdlBuilder.QuoteIdentifier(pkColumn.PhysicalName)}";
            joinClauses.Add(joinClause);

            // Add structured join info for query builder integration
            joins.Add(new LookupJoinInfo
            {
                TargetTablePhysical = targetTable.PhysicalName,
                TargetTableAlias = alias,
                SourceColumnPhysical = relationColumn.PhysicalName,
                TargetColumnPhysical = pkColumn.PhysicalName
            });

            // Build SELECT expression: alias."target_physical" AS "lookup_column"
            var selectExpr = $"{alias}.{DdlBuilder.QuoteIdentifier(targetColumn.PhysicalName)}";
            selectExpressions[lookup.ColumnName] = selectExpr;
        }

        return new LookupQueryExpansion
        {
            JoinClauses = joinClauses,
            Joins = joins,
            SelectExpressions = selectExpressions,
            TableAliases = tableAliases
        };
    }

    public async Task<LookupValidationResult> ValidateLookupConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        LookupColumnConfig config,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        // Find relation column in source table
        var relationColumn = sourceTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.RelationColumn);

        if (relationColumn == null)
        {
            errors.Add($"Relation column '{config.RelationColumn}' not found in table '{sourceTable.LogicalName}'.");
            return LookupValidationResult.Invalid([.. errors]);
        }

        // Get target table
        var targetTable = await _metadataRepository.GetTableByNameAsync(
            projectId, config.TargetTable, includeColumns: true, cancellationToken);

        if (targetTable == null)
        {
            errors.Add($"Target table '{config.TargetTable}' not found.");
            return LookupValidationResult.Invalid([.. errors]);
        }

        // Find target column
        var targetColumn = targetTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.TargetColumn);

        if (targetColumn == null)
        {
            errors.Add($"Target column '{config.TargetColumn}' not found in table '{config.TargetTable}'.");
            return LookupValidationResult.Invalid([.. errors]);
        }

        // Verify relation column type is compatible (should be UUID for FK)
        if (relationColumn.DataType != MorphDataType.Uuid &&
            relationColumn.DataType != MorphDataType.Relation &&
            relationColumn.DataType != MorphDataType.BigInteger &&
            relationColumn.DataType != MorphDataType.Integer)
        {
            errors.Add($"Relation column '{config.RelationColumn}' should be a UUID, Relation, or integer type.");
        }

        if (errors.Count > 0)
        {
            return LookupValidationResult.Invalid([.. errors]);
        }

        return LookupValidationResult.Valid(targetTable, targetColumn, relationColumn);
    }

    public async Task<ColumnMetadata?> GetTargetColumnMetadataAsync(
        Guid projectId,
        LookupColumnConfig config,
        CancellationToken cancellationToken = default)
    {
        var targetTable = await _metadataRepository.GetTableByNameAsync(
            projectId, config.TargetTable, includeColumns: true, cancellationToken);

        if (targetTable == null)
            return null;

        return targetTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.TargetColumn);
    }

    private string GenerateTableAlias(string tableName)
    {
        _aliasCounter++;
        // Use first few chars of table name + counter for readability
        var prefix = tableName.Length > 3 ? tableName[..3] : tableName;
        return $"lkp_{prefix}_{_aliasCounter}";
    }
}
