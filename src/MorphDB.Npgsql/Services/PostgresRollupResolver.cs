using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
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
        var subqueries = new List<RollupSubqueryInfo>();

        // Get primary key column from source table
        var pkColumn = sourceTable.Columns
            .FirstOrDefault(c => c.IsPrimaryKey || c.LogicalName == "_id");

        if (pkColumn == null)
        {
            return new RollupQueryExpansion();
        }

        foreach (var rollup in rollupColumns)
        {
            var validation = await ValidateRollupConfigAsync(
                projectId, sourceTable, rollup.Config, cancellationToken);

            if (!validation.IsValid)
                continue;

            var targetTable = validation.TargetTable!;
            var fkColumn = validation.ForeignKeyColumn!;
            var sourceColumn = validation.SourceColumn;

            // Build the correlated subquery
            var subquery = BuildAggregationSubquery(
                targetTable.PhysicalName,
                fkColumn.PhysicalName,
                pkColumn.PhysicalName,
                sourceColumn?.PhysicalName,
                rollup.Config);

            subqueryExpressions[rollup.ColumnName] = subquery;

            // Add structured info for query builder
            subqueries.Add(new RollupSubqueryInfo
            {
                ColumnName = rollup.ColumnName,
                TargetTablePhysical = targetTable.PhysicalName,
                TargetTableLogical = targetTable.LogicalName,
                ForeignKeyColumnPhysical = fkColumn.PhysicalName,
                SourceColumnPhysical = sourceColumn?.PhysicalName ?? "*",
                ParentKeyColumnPhysical = pkColumn.PhysicalName,
                Aggregation = rollup.Config.Aggregation,
                FilterClause = BuildFilterClause(rollup.Config.Filter, targetTable),
                OrderByClause = rollup.Config.OrderBy,
                Delimiter = rollup.Config.Delimiter
            });
        }

        return new RollupQueryExpansion
        {
            SubqueryExpressions = subqueryExpressions,
            Subqueries = subqueries
        };
    }

    public async Task<RollupValidationResult> ValidateRollupConfigAsync(
        Guid projectId,
        TableMetadata sourceTable,
        RollupColumnConfig config,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        // Get target table
        var targetTable = await _metadataRepository.GetTableByNameAsync(
            projectId, config.TargetTable, includeColumns: true, cancellationToken);

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

        if (errors.Count > 0)
        {
            return RollupValidationResult.Invalid([.. errors]);
        }

        return RollupValidationResult.Valid(targetTable, sourceColumn, fkColumn);
    }

    private static string BuildAggregationSubquery(
        string targetTablePhysical,
        string fkColumnPhysical,
        string pkColumnPhysical,
        string? sourceColumnPhysical,
        RollupColumnConfig config)
    {
        var quotedTarget = DdlBuilder.QuoteIdentifier(targetTablePhysical);
        var quotedFk = DdlBuilder.QuoteIdentifier(fkColumnPhysical);
        var quotedPk = DdlBuilder.QuoteIdentifier(pkColumnPhysical);
        var quotedSource = sourceColumnPhysical != null && sourceColumnPhysical != "*"
            ? DdlBuilder.QuoteIdentifier(sourceColumnPhysical)
            : null;

        // Build the aggregate expression
        var aggregateExpr = BuildAggregateExpression(config.Aggregation, quotedSource, config);

        // Build WHERE clause for correlation
        var whereClause = $"sub.{quotedFk} = base_table.{quotedPk}";

        // Add filter if present
        var filterClause = config.Filter != null
            ? $" AND {BuildSimpleFilter(config.Filter)}"
            : "";

        // Build the correlated subquery
        return $"(SELECT {aggregateExpr} FROM {quotedTarget} AS sub WHERE {whereClause}{filterClause})";
    }

    private static string BuildAggregateExpression(
        RollupAggregation aggregation,
        string? quotedColumn,
        RollupColumnConfig config)
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
            RollupAggregation.StringConcat => BuildStringAgg(quotedColumn!, config),
            RollupAggregation.ArrayValues => $"ARRAY_AGG({quotedColumn}{BuildOrderByClause(config.OrderBy)})",
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

    private static string BuildStringAgg(string quotedColumn, RollupColumnConfig config)
    {
        var delimiter = config.Delimiter ?? ", ";
        var escapedDelimiter = delimiter.Replace("'", "''");
        var orderBy = BuildOrderByClause(config.OrderBy);
        return $"STRING_AGG({quotedColumn}::text, '{escapedDelimiter}'{orderBy})";
    }

    private static string BuildOrderByClause(string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return "";
        return $" ORDER BY {orderBy}";
    }

    private static string? BuildFilterClause(RollupFilter? filter, TableMetadata targetTable)
    {
        if (filter == null)
            return null;

        var column = targetTable.Columns
            .FirstOrDefault(c => c.LogicalName == filter.Field);

        if (column == null)
            return null;

        return BuildSimpleFilter(filter);
    }

    private static string BuildSimpleFilter(RollupFilter filter)
    {
        var quotedField = DdlBuilder.QuoteIdentifier(filter.Field);
        var value = FormatFilterValue(filter.Value);

        return filter.Operator switch
        {
            FilterOperator.Equals => $"sub.{quotedField} = {value}",
            FilterOperator.NotEquals => $"sub.{quotedField} <> {value}",
            FilterOperator.GreaterThan => $"sub.{quotedField} > {value}",
            FilterOperator.GreaterThanOrEquals => $"sub.{quotedField} >= {value}",
            FilterOperator.LessThan => $"sub.{quotedField} < {value}",
            FilterOperator.LessThanOrEquals => $"sub.{quotedField} <= {value}",
            FilterOperator.Contains => $"sub.{quotedField} ILIKE '%' || {value} || '%'",
            FilterOperator.StartsWith => $"sub.{quotedField} ILIKE {value} || '%'",
            FilterOperator.EndsWith => $"sub.{quotedField} ILIKE '%' || {value}",
            FilterOperator.IsNull => $"sub.{quotedField} IS NULL",
            FilterOperator.IsNotNull => $"sub.{quotedField} IS NOT NULL",
            FilterOperator.In => $"sub.{quotedField} = ANY({value})",
            FilterOperator.NotIn => $"NOT (sub.{quotedField} = ANY({value}))",
            _ => $"sub.{quotedField} = {value}"
        };
    }

    private static string FormatFilterValue(object? value)
    {
        if (value == null)
            return "NULL";

        return value switch
        {
            string s => $"'{s.Replace("'", "''")}'",
            bool b => b ? "true" : "false",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'::timestamptz",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss}'::timestamptz",
            Guid g => $"'{g}'::uuid",
            _ => value.ToString() ?? "NULL"
        };
    }

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
