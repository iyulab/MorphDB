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

            // A target that cannot answer yet, under WhenTargetMissing = Null: no row matches, so the
            // column reads null — typed as declared, so a filter or sort over it still plans.
            if (validation.IsTargetMissing)
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
        // What is wrong on this side fails whatever the target: the relation column and the shape of
        // the order are this declaration's own.
        var relationColumn = sourceTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.RelationColumn);

        if (relationColumn == null)
        {
            return LookupValidationResult.Invalid(
                $"Relation column '{config.RelationColumn}' not found in table '{sourceTable.LogicalName}'.");
        }

        var syntaxErrors = DeclaredOrder.SyntaxErrors(config.OrderBy).ToArray();
        if (syntaxErrors.Length > 0)
        {
            return LookupValidationResult.Invalid(syntaxErrors);
        }

        // What the target cannot answer — the table, or a column the declaration names in it — fails,
        // or under WhenTargetMissing = Null reads as no match until the target answers.
        var targetErrors = new List<string>();

        // Get target table — the table in hand when the lookup reads its own table (it may not be
        // stored yet: a table is checked while it is being declared)
        var targetTable = config.TargetTable == sourceTable.LogicalName
            ? sourceTable
            : await _metadataRepository.GetTableByNameAsync(projectId, config.TargetTable, includeColumns: true, cancellationToken);

        ColumnMetadata? targetColumn = null;
        string? matchPhysical = null;

        if (targetTable == null)
        {
            targetErrors.Add($"Target table '{config.TargetTable}' not found.");
        }
        else
        {
            // The column read must be stored: a derived column of the target is computed by its own read.
            targetColumn = targetTable.Columns
                .FirstOrDefault(c => c.LogicalName == config.TargetColumn && !c.IsDerived);

            if (targetColumn == null)
            {
                targetErrors.Add($"Target column '{config.TargetColumn}' is not a stored column of '{config.TargetTable}'.");
            }

            // The value is matched against the named column, else the target column of the relation
            // declared on the relation column, else the target's _id. Whether the two types compare is
            // PostgreSQL's to say when the lookup is declared.
            var matchName = config.MatchColumn
                ?? (relationColumn.ForeignKey is { } fk && fk.TargetTable == targetTable.LogicalName ? fk.TargetColumn : null)
                ?? "_id";
            var matchColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == matchName && !c.IsDerived);
            matchPhysical = matchColumn?.PhysicalName ?? (SystemColumns.IsSystemColumn(matchName) ? matchName : null);

            if (matchPhysical == null)
            {
                targetErrors.Add($"Match column '{matchName}' is not a stored column of '{config.TargetTable}'.");
            }

            targetErrors.AddRange(DeclaredOrder.ColumnErrors(config.OrderBy, targetTable));
        }

        if (targetErrors.Count > 0)
        {
            return config.WhenTargetMissing == LookupTargetMissing.Null
                ? LookupValidationResult.TargetMissing(relationColumn)
                : LookupValidationResult.Invalid([.. targetErrors]);
        }

        return LookupValidationResult.Valid(targetTable!, targetColumn!, relationColumn, matchPhysical!);
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
