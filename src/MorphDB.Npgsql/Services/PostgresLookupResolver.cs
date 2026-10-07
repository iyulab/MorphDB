using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of lookup field resolution.
/// Generates JOINs for lookup columns during query execution.
/// </summary>
public sealed class PostgresLookupResolver : ILookupResolver
{
    private readonly IMetadataRepository _metadataRepository;

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

        var selectExpressions = new Dictionary<string, string>();
        var aliasCounter = 0;

        foreach (var lookup in lookupColumns)
        {
            // A lookup that cannot be computed fails the read that needs it, naming why. It used to
            // be skipped, so the column silently disappeared from every row.
            var validation = await ValidateLookupConfigAsync(
                projectId, sourceTable, lookup.Config, cancellationToken);

            if (!validation.IsValid)
            {
                throw new SchemaException(
                    "INVALID_EXPRESSION",
                    $"Lookup column '{lookup.ColumnName}' cannot be computed: {string.Join(" ", validation.Errors)}");
            }

            // A target that does not exist yet, under WhenTargetMissing = Null: no row matches, so
            // the column reads null — typed as declared, so a filter or sort over it still plans.
            if (validation.IsTargetAbsent)
            {
                selectExpressions[lookup.ColumnName] = lookup.DataType is { } type
                    ? $"NULL::{TypeMapper.ToNativeType(type)}"
                    : "NULL";
                continue;
            }

            // A correlated subquery, not a join: several target rows may match a value that is not
            // the target's key, and a join would repeat the row once per match. The declared order
            // (by default the target's _id) chooses the one read. Aliases are numbered per read.
            var alias = $"lkp_{++aliasCounter}";
            var target = validation.TargetTable!;
            var order = string.IsNullOrWhiteSpace(lookup.Config.OrderBy) ? "_id asc" : lookup.Config.OrderBy;

            selectExpressions[lookup.ColumnName] =
                $"(SELECT {alias}.{DdlBuilder.QuoteIdentifier(validation.TargetColumn!.PhysicalName)} " +
                $"FROM {DdlBuilder.QuoteIdentifier(target.PhysicalName)} AS {alias} " +
                $"WHERE {alias}.{DdlBuilder.QuoteIdentifier(validation.MatchColumnPhysical!)} = " +
                $"base_table.{DdlBuilder.QuoteIdentifier(validation.RelationColumn!.PhysicalName)}" +
                $"{DeclaredOrder.Sql(order, target, alias)} LIMIT 1)";
        }

        return new LookupQueryExpansion { SelectExpressions = selectExpressions };
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

        // Get target table — the table in hand when the lookup reads its own table (it may not be
        // stored yet: a table is checked while it is being declared)
        var targetTable = config.TargetTable == sourceTable.LogicalName
            ? sourceTable
            : await _metadataRepository.GetTableByNameAsync(projectId, config.TargetTable, includeColumns: true, cancellationToken);

        if (targetTable == null)
        {
            if (config.WhenTargetMissing == LookupTargetMissing.Null)
            {
                return LookupValidationResult.TargetAbsent(relationColumn);
            }

            errors.Add($"Target table '{config.TargetTable}' not found.");
            return LookupValidationResult.Invalid([.. errors]);
        }

        // The column read must be stored: a derived column of the target is computed by its own read.
        var targetColumn = targetTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.TargetColumn && !c.IsDerived);

        if (targetColumn == null)
        {
            errors.Add($"Target column '{config.TargetColumn}' is not a stored column of '{config.TargetTable}'.");
            return LookupValidationResult.Invalid([.. errors]);
        }

        // The value is matched against the named column, else the target column of the relation
        // declared on the relation column, else the target's _id. Whether the two types compare is
        // PostgreSQL's to say when the lookup is declared.
        var matchName = config.MatchColumn
            ?? (relationColumn.ForeignKey is { } fk && fk.TargetTable == targetTable.LogicalName ? fk.TargetColumn : null)
            ?? "_id";
        var matchColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == matchName && !c.IsDerived);
        var matchPhysical = matchColumn?.PhysicalName ?? (SystemColumns.IsSystemColumn(matchName) ? matchName : null);

        if (matchPhysical == null)
        {
            errors.Add($"Match column '{matchName}' is not a stored column of '{config.TargetTable}'.");
        }

        errors.AddRange(DeclaredOrder.Errors(config.OrderBy, targetTable));

        if (errors.Count > 0)
        {
            return LookupValidationResult.Invalid([.. errors]);
        }

        return LookupValidationResult.Valid(targetTable, targetColumn, relationColumn, matchPhysical!);
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
}
