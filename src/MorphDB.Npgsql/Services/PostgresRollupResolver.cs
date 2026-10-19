using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of rollup field resolution.
/// Generates correlated subqueries for aggregate values from child records.
/// </summary>
public sealed class PostgresRollupResolver : IRollupResolver
{
    private readonly IMetadataRepository _metadataRepository;

    public PostgresRollupResolver(IMetadataRepository metadataRepository)
    {
        _metadataRepository = metadataRepository;
    }

    public async Task<RollupQueryExpansion> BuildRollupExpansionAsync(
        Guid projectId,
        TableMetadata sourceTable,
        IReadOnlyList<RollupColumnInfo> rollupColumns,
        CancellationToken cancellationToken = default)
    {
        if (rollupColumns.Count == 0)
        {
            return new RollupQueryExpansion();
        }

        var subqueryExpressions = new Dictionary<string, string>();

        // Get primary key column from source table
        var pkColumn = sourceTable.Columns
            .FirstOrDefault(c => c.IsPrimaryKey || c.LogicalName == "_id")
            ?? throw new SchemaException("INVALID_EXPRESSION", $"Table '{sourceTable.LogicalName}' has no primary key to roll up into.");

        foreach (var rollup in rollupColumns)
        {
            // A rollup that cannot be computed fails the read that needs it, naming why. It used to
            // be skipped, so the column silently disappeared from every row.
            var validation = await ValidateRollupConfigAsync(
                projectId, sourceTable, rollup.Config, cancellationToken);

            if (!validation.IsValid)
            {
                throw new SchemaException(
                    "INVALID_EXPRESSION",
                    $"Rollup column '{rollup.ColumnName}' cannot be computed: {string.Join(" ", validation.Errors)}");
            }

            // A target that cannot answer yet, under WhenTargetMissing = Empty: no rows to summarise.
            if (validation.IsTargetMissing)
            {
                subqueryExpressions[rollup.ColumnName] = EmptyValue(rollup);
                continue;
            }

            subqueryExpressions[rollup.ColumnName] = BuildAggregationSubquery(
                validation.TargetTable!,
                validation.ForeignKeyColumn!.PhysicalName,
                pkColumn.PhysicalName,
                validation.SourceColumn?.PhysicalName,
                rollup.Config);
        }

        return new RollupQueryExpansion
        {
            SubqueryExpressions = subqueryExpressions
        };
    }

    public async Task<RollupValidationResult> ValidateRollupConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        RollupColumnConfig config,
        CancellationToken cancellationToken = default)
    {
        // What is wrong in the declaration itself fails whatever the target: the shape of the order.
        var syntaxErrors = DeclaredOrder.SyntaxErrors(config.OrderBy).ToArray();
        if (syntaxErrors.Length > 0)
        {
            return RollupValidationResult.Invalid(syntaxErrors);
        }

        // Get target table — the table in hand when the rollup reads its own table (it may not be
        // stored yet: a table is checked while it is being declared)
        var targetTable = config.TargetTable == sourceTable.LogicalName
            ? sourceTable
            : await _metadataRepository.GetTableByNameAsync(projectId, config.TargetTable, includeColumns: true, cancellationToken);

        // What the target cannot answer — the table, or a column the rollup names in it — fails, or
        // under WhenTargetMissing = Empty reads as no rows to summarise until the target answers.
        var targetErrors = new List<string>();
        var declarationErrors = new List<string>();
        ColumnMetadata? fkColumn = null;
        ColumnMetadata? sourceColumn = null;

        if (targetTable == null)
        {
            targetErrors.Add($"Target table '{config.TargetTable}' not found.");
        }
        else
        {
            fkColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == config.ForeignKeyColumn);
            if (fkColumn == null)
            {
                targetErrors.Add($"Foreign key column '{config.ForeignKeyColumn}' not found in table '{config.TargetTable}'.");
            }
            else if (fkColumn.DataType != MorphDataType.Uuid &&
                fkColumn.DataType != MorphDataType.Relation &&
                fkColumn.DataType != MorphDataType.BigInteger &&
                fkColumn.DataType != MorphDataType.Integer)
            {
                declarationErrors.Add($"Foreign key column '{config.ForeignKeyColumn}' should be a UUID, Relation, or integer type.");
            }

            // The source column may be "*" for COUNT.
            if (config.SourceColumn != "*")
            {
                sourceColumn = targetTable.Columns.FirstOrDefault(c => c.LogicalName == config.SourceColumn);
                if (sourceColumn == null)
                {
                    targetErrors.Add($"Source column '{config.SourceColumn}' not found in table '{config.TargetTable}'.");
                }
                else if (!IsAggregationCompatible(config.Aggregation, sourceColumn.DataType))
                {
                    declarationErrors.Add($"Aggregation '{config.Aggregation}' is not compatible with column type '{sourceColumn.DataType}'.");
                }
            }

            foreach (var error in FilterErrors(config.Filter, targetTable))
            {
                (error.StartsWith("Filter value:", StringComparison.Ordinal) ? declarationErrors : targetErrors).Add(error);
            }

            targetErrors.AddRange(DeclaredOrder.ColumnErrors(config.OrderBy, targetTable));
        }

        // A target that is there but declared against wrongly is not a missing target.
        if (declarationErrors.Count > 0)
        {
            return RollupValidationResult.Invalid([.. declarationErrors, .. targetErrors]);
        }

        if (targetErrors.Count > 0)
        {
            return config.WhenTargetMissing == RollupTargetMissing.Empty
                ? RollupValidationResult.TargetMissing()
                : RollupValidationResult.Invalid([.. targetErrors]);
        }

        return RollupValidationResult.Valid(targetTable!, sourceColumn, fkColumn!);
    }

    /// <summary>
    /// What a rollup reads over no rows: a count is 0, every other aggregation has nothing to summarise
    /// and reads null — as SQL's aggregates read over an empty set. A row no target row points at reads
    /// this through its subquery; a rollup whose target cannot answer reads it directly.
    /// </summary>
    private static string EmptyValue(RollupColumnInfo rollup) => rollup.Config.Aggregation switch
    {
        RollupAggregation.Count or RollupAggregation.CountValues or RollupAggregation.CountEmpty => "0::bigint",
        _ => rollup.DataType is { } type ? $"NULL::{TypeMapper.ToNativeType(type)}" : "NULL",
    };

    private static string BuildAggregationSubquery(
        TableMetadata targetTable,
        string fkColumnPhysical,
        string pkColumnPhysical,
        string? sourceColumnPhysical,
        RollupColumnConfig config)
    {
        var quotedTarget = DdlBuilder.QuoteIdentifier(targetTable.PhysicalName);
        var quotedFk = DdlBuilder.QuoteIdentifier(fkColumnPhysical);
        var quotedPk = DdlBuilder.QuoteIdentifier(pkColumnPhysical);
        var quotedSource = sourceColumnPhysical != null && sourceColumnPhysical != "*"
            ? $"sub.{DdlBuilder.QuoteIdentifier(sourceColumnPhysical)}"
            : null;

        var orderBy = DeclaredOrder.Sql(config.OrderBy, targetTable, "sub");
        var aggregateExpr = BuildAggregateExpression(config.Aggregation, quotedSource, config, orderBy);

        // A child row a soft delete has marked does not count, as it does not list.
        var whereClause = targetTable.SoftDeleteEnabled
            ? $"sub.{quotedFk} = base_table.{quotedPk} AND sub.{SystemColumns.DeletedAt} IS NULL"
            : $"sub.{quotedFk} = base_table.{quotedPk}";
        var filterClause = config.Filter != null
            ? $" AND {BuildFilter(config.Filter, targetTable)}"
            : "";

        return $"(SELECT {aggregateExpr} FROM {quotedTarget} AS sub WHERE {whereClause}{filterClause})";
    }

    private static string BuildAggregateExpression(
        RollupAggregation aggregation,
        string? quotedColumn,
        RollupColumnConfig config,
        string orderBy)
    {
        return aggregation switch
        {
            RollupAggregation.Count => "COUNT(*)",
            RollupAggregation.CountValues => $"COUNT({quotedColumn})",
            RollupAggregation.CountEmpty => $"COUNT(*) - COUNT({quotedColumn})",
            RollupAggregation.Sum => $"SUM({quotedColumn})",
            RollupAggregation.Average => $"AVG({quotedColumn})",
            RollupAggregation.Min => $"MIN({quotedColumn})",
            RollupAggregation.Max => $"MAX({quotedColumn})",
            RollupAggregation.StringConcat => $"STRING_AGG({quotedColumn}::text, {SqlLiteral.Render(config.Delimiter ?? ", ")}{orderBy})",
            RollupAggregation.ArrayValues => $"ARRAY_AGG({quotedColumn}{orderBy})",
            RollupAggregation.PercentChecked => $"ROUND(100.0 * COUNT(CASE WHEN {quotedColumn} = true THEN 1 END) / NULLIF(COUNT(*), 0), 2)",
            RollupAggregation.PercentUnchecked => $"ROUND(100.0 * COUNT(CASE WHEN {quotedColumn} = false THEN 1 END) / NULLIF(COUNT(*), 0), 2)",
            RollupAggregation.EarliestDate => $"MIN({quotedColumn})",
            RollupAggregation.LatestDate => $"MAX({quotedColumn})",
            RollupAggregation.DateRange => $"(MAX({quotedColumn}) - MIN({quotedColumn}))",
            RollupAggregation.AllTrue => $"BOOL_AND({quotedColumn})",
            RollupAggregation.AnyTrue => $"BOOL_OR({quotedColumn})",
            _ => "COUNT(*)"
        };
    }

    private static IEnumerable<string> FilterErrors(RollupFilter? filter, TableMetadata targetTable)
    {
        if (filter is null)
            yield break;

        if (StoredColumn(targetTable, filter.Field) is null)
        {
            yield return $"Filter column '{filter.Field}' is not a stored column of '{targetTable.LogicalName}'.";
            yield break;
        }

        string? error = null;
        try
        {
            _ = BuildFilter(filter, targetTable);
        }
        catch (ValidationException ex)
        {
            error = $"Filter value: {ex.Message}";
        }

        if (error is not null)
            yield return error;
    }

    /// <summary>
    /// The filter as SQL over the target table: its column by physical name, its value a rendered
    /// literal (<see cref="SqlLiteral"/>). The field used to be emitted in its logical name — so a
    /// filtered rollup could never run — and the value as raw text.
    /// </summary>
    private static string BuildFilter(RollupFilter filter, TableMetadata targetTable)
    {
        var column = $"sub.{DdlBuilder.QuoteIdentifier(StoredColumn(targetTable, filter.Field)!.PhysicalName)}";

        return filter.Operator switch
        {
            FilterOperator.Equals => $"{column} = {SqlLiteral.Render(filter.Value)}",
            FilterOperator.NotEquals => $"{column} <> {SqlLiteral.Render(filter.Value)}",
            FilterOperator.GreaterThan => $"{column} > {SqlLiteral.Render(filter.Value)}",
            FilterOperator.GreaterThanOrEquals => $"{column} >= {SqlLiteral.Render(filter.Value)}",
            FilterOperator.LessThan => $"{column} < {SqlLiteral.Render(filter.Value)}",
            FilterOperator.LessThanOrEquals => $"{column} <= {SqlLiteral.Render(filter.Value)}",
            FilterOperator.Contains => $"{column} ILIKE {SqlLiteral.Render(LikePattern.Contains(filter.Value))}",
            FilterOperator.StartsWith => $"{column} ILIKE {SqlLiteral.Render(LikePattern.StartsWith(filter.Value))}",
            FilterOperator.EndsWith => $"{column} ILIKE {SqlLiteral.Render(LikePattern.EndsWith(filter.Value))}",
            FilterOperator.IsNull => $"{column} IS NULL",
            FilterOperator.IsNotNull => $"{column} IS NOT NULL",
            FilterOperator.In => SqlLiteral.RenderList(filter.Value) is { Count: > 0 } values ? $"{column} IN ({string.Join(", ", values)})" : "FALSE",
            FilterOperator.NotIn => SqlLiteral.RenderList(filter.Value) is { Count: > 0 } values ? $"{column} NOT IN ({string.Join(", ", values)})" : "TRUE",
            _ => throw new ValidationException("operator", $"a rollup filter does not take '{filter.Operator}'.")
        };
    }

    private static ColumnMetadata? StoredColumn(TableMetadata table, string logicalName) =>
        table.Columns.FirstOrDefault(c => c.LogicalName == logicalName && !c.IsDerived);

    private static bool IsAggregationCompatible(RollupAggregation aggregation, MorphDataType dataType)
    {
        return aggregation switch
        {
            // Count operations work with any type
            RollupAggregation.Count or
            RollupAggregation.CountValues or
            RollupAggregation.CountEmpty => true,

            // Numeric aggregations require numeric types
            RollupAggregation.Sum or
            RollupAggregation.Average => dataType is
                MorphDataType.Integer or
                MorphDataType.BigInteger or
                MorphDataType.Decimal,

            // Min/Max work with comparable types
            RollupAggregation.Min or
            RollupAggregation.Max => dataType is
                MorphDataType.Integer or
                MorphDataType.BigInteger or
                MorphDataType.Decimal or
                MorphDataType.Text or
                MorphDataType.LongText or
                MorphDataType.Date or
                MorphDataType.DateTime or
                MorphDataType.Time or
                MorphDataType.CreatedTime or
                MorphDataType.ModifiedTime,

            // String aggregation requires text-like types
            RollupAggregation.StringConcat => dataType is
                MorphDataType.Text or
                MorphDataType.LongText or
                MorphDataType.Email or
                MorphDataType.Url or
                MorphDataType.Phone,

            // Array aggregation works with most types
            RollupAggregation.ArrayValues => true,

            // Boolean aggregations require boolean type
            RollupAggregation.PercentChecked or
            RollupAggregation.PercentUnchecked or
            RollupAggregation.AllTrue or
            RollupAggregation.AnyTrue => dataType is MorphDataType.Boolean,

            // Date range operations require date types
            RollupAggregation.EarliestDate or
            RollupAggregation.LatestDate or
            RollupAggregation.DateRange => dataType is
                MorphDataType.Date or
                MorphDataType.DateTime or
                MorphDataType.CreatedTime or
                MorphDataType.ModifiedTime,

            _ => false
        };
    }
}
