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
        var errors = new List<string>();

        // Get target table — the table in hand when the rollup reads its own table (it may not be
        // stored yet: a table is checked while it is being declared)
        var targetTable = config.TargetTable == sourceTable.LogicalName
            ? sourceTable
            : await _metadataRepository.GetTableByNameAsync(projectId, config.TargetTable, includeColumns: true, cancellationToken);

        if (targetTable == null)
        {
            errors.Add($"Target table '{config.TargetTable}' not found.");
            return RollupValidationResult.Invalid([.. errors]);
        }

        // Find foreign key column in target table
        var fkColumn = targetTable.Columns
            .FirstOrDefault(c => c.LogicalName == config.ForeignKeyColumn);

        if (fkColumn == null)
        {
            errors.Add($"Foreign key column '{config.ForeignKeyColumn}' not found in table '{config.TargetTable}'.");
            return RollupValidationResult.Invalid([.. errors]);
        }

        // Find source column (column to aggregate) - may be "*" for COUNT
        ColumnMetadata? sourceColumn = null;
        if (config.SourceColumn != "*")
        {
            sourceColumn = targetTable.Columns
                .FirstOrDefault(c => c.LogicalName == config.SourceColumn);

            if (sourceColumn == null)
            {
                errors.Add($"Source column '{config.SourceColumn}' not found in table '{config.TargetTable}'.");
                return RollupValidationResult.Invalid([.. errors]);
            }

            // Validate aggregation is compatible with column type
            if (!IsAggregationCompatible(config.Aggregation, sourceColumn.DataType))
            {
                errors.Add($"Aggregation '{config.Aggregation}' is not compatible with column type '{sourceColumn.DataType}'.");
            }
        }

        // Verify FK column type is compatible
        if (fkColumn.DataType != MorphDataType.Uuid &&
            fkColumn.DataType != MorphDataType.Relation &&
            fkColumn.DataType != MorphDataType.BigInteger &&
            fkColumn.DataType != MorphDataType.Integer)
        {
            errors.Add($"Foreign key column '{config.ForeignKeyColumn}' should be a UUID, Relation, or integer type.");
        }

        errors.AddRange(FilterErrors(config.Filter, targetTable));
        errors.AddRange(OrderErrors(config.OrderBy, targetTable));

        if (errors.Count > 0)
        {
            return RollupValidationResult.Invalid([.. errors]);
        }

        return RollupValidationResult.Valid(targetTable, sourceColumn, fkColumn);
    }

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

        var orderBy = BuildOrderByClause(config.OrderBy, targetTable);
        var aggregateExpr = BuildAggregateExpression(config.Aggregation, quotedSource, config, orderBy);

        var whereClause = $"sub.{quotedFk} = base_table.{quotedPk}";
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
            RollupAggregation.Sum => $"COALESCE(SUM({quotedColumn}), 0)",
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

    /// <summary>
    /// The order a <c>stringConcat</c> or <c>arrayValues</c> rollup collects its values in:
    /// comma-separated <c>column [asc|desc]</c> terms over the target table's stored columns. It is
    /// parsed, never pasted — it used to reach SQL as the caller wrote it, in logical names.
    /// </summary>
    private static string BuildOrderByClause(string? orderBy, TableMetadata targetTable)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return "";

        var terms = ParseOrder(orderBy).Select(t =>
        {
            var column = StoredColumn(targetTable, t.Column)!;
            return $"sub.{DdlBuilder.QuoteIdentifier(column.PhysicalName)}{(t.Descending ? " DESC" : " ASC")}";
        });
        return $" ORDER BY {string.Join(", ", terms)}";
    }

    private static IEnumerable<string> OrderErrors(string? orderBy, TableMetadata targetTable)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            yield break;

        foreach (var term in orderBy.Split(','))
        {
            var parts = term.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 ||
                (parts.Length == 2 && !parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase) && !parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"Order '{orderBy}' is not a list of 'column [asc|desc]'.";
                yield break;
            }

            if (StoredColumn(targetTable, parts[0]) is null)
            {
                yield return $"Order column '{parts[0]}' is not a stored column of '{targetTable.LogicalName}'.";
            }
        }
    }

    private static IEnumerable<(string Column, bool Descending)> ParseOrder(string orderBy) =>
        orderBy.Split(',').Select(term =>
        {
            var parts = term.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (parts[0], parts.Length == 2 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
        });

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
