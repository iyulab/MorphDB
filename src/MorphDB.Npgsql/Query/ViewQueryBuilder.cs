using System.Globalization;
using System.Text;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// Builds SQL SELECT statements from view definitions.
/// Handles logical-to-physical name translation for views.
/// </summary>
public sealed class ViewQueryBuilder
{
    private readonly IMetadataRepository _metadataRepository;
    private readonly Guid _tenantId;
    private readonly Dictionary<string, TableMetadata> _tableCache = new();

    public ViewQueryBuilder(IMetadataRepository metadataRepository, Guid tenantId)
    {
        _metadataRepository = metadataRepository;
        _tenantId = tenantId;
    }

    /// <summary>
    /// Builds a SQL SELECT statement from a view definition.
    /// </summary>
    public async Task<string> BuildSelectStatementAsync(
        ViewDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // Load base table metadata
        var baseTable = await GetTableMetadataAsync(definition.BaseTable, cancellationToken);
        if (baseTable == null)
        {
            throw new InvalidOperationException($"Base table '{definition.BaseTable}' not found.");
        }

        // Build SELECT clause
        sb.Append("SELECT ");
        if (definition.Distinct)
        {
            sb.Append("DISTINCT ");
        }

        if (definition.Columns.Count == 0)
        {
            sb.Append('*');
        }
        else
        {
            var columnExpressions = new List<string>();
            foreach (var col in definition.Columns)
            {
                columnExpressions.Add(await BuildColumnExpressionAsync(col, baseTable, cancellationToken));
            }
            sb.Append(string.Join(", ", columnExpressions));
        }

        // Build FROM clause
        sb.Append("\nFROM ");
        sb.Append(DdlBuilder.QuoteIdentifier(baseTable.PhysicalName));
        sb.Append(" AS base_table");

        // Build JOIN clauses
        foreach (var join in definition.Joins)
        {
            var joinTable = await GetTableMetadataAsync(join.Table, cancellationToken);
            if (joinTable == null)
            {
                throw new InvalidOperationException($"Join table '{join.Table}' not found.");
            }

            sb.Append('\n');
            sb.Append(MapJoinType(join.JoinType));
            sb.Append(' ');
            sb.Append(DdlBuilder.QuoteIdentifier(joinTable.PhysicalName));
            if (!string.IsNullOrEmpty(join.Alias))
            {
                sb.Append(" AS ");
                sb.Append(DdlBuilder.QuoteIdentifier(join.Alias));
            }
            sb.Append(" ON ");
            sb.Append(await TranslateConditionAsync(join.Condition, cancellationToken));
        }

        // Build WHERE clause
        if (definition.Filters.Count > 0)
        {
            sb.Append("\nWHERE ");
            var filterConditions = new List<string>();
            for (int i = 0; i < definition.Filters.Count; i++)
            {
                var filter = definition.Filters[i];
                var condition = await BuildFilterConditionAsync(filter, baseTable, cancellationToken);
                if (i > 0)
                {
                    sb.Append(filter.LogicalOp == LogicalOperator.And ? " AND " : " OR ");
                }
                filterConditions.Add(condition);
            }
            sb.Append(string.Join("", filterConditions.Select((c, i) => i == 0 ? c : c)));
        }

        // Add tenant isolation
        var tenantCondition = $"base_table.\"tenant_id\" = '{_tenantId}'";
        if (definition.Filters.Count > 0)
        {
            sb.Append(" AND ");
            sb.Append(tenantCondition);
        }
        else
        {
            sb.Append("\nWHERE ");
            sb.Append(tenantCondition);
        }

        // Build GROUP BY clause
        if (definition.GroupBy.Count > 0)
        {
            sb.Append("\nGROUP BY ");
            var groupByColumns = new List<string>();
            foreach (var col in definition.GroupBy)
            {
                var physicalName = await TranslateColumnNameAsync(col, baseTable, cancellationToken);
                groupByColumns.Add(physicalName);
            }
            sb.Append(string.Join(", ", groupByColumns));
        }

        // Build ORDER BY clause
        if (definition.OrderBy.Count > 0)
        {
            sb.Append("\nORDER BY ");
            var orderClauses = new List<string>();
            foreach (var order in definition.OrderBy)
            {
                var physicalName = await TranslateColumnNameAsync(order.Column, baseTable, cancellationToken);
                var direction = order.Descending ? " DESC" : " ASC";
                var nulls = order.NullOrdering == NullOrdering.First ? " NULLS FIRST" : " NULLS LAST";
                orderClauses.Add($"{physicalName}{direction}{nulls}");
            }
            sb.Append(string.Join(", ", orderClauses));
        }

        // Build LIMIT clause
        if (definition.Limit.HasValue)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nLIMIT {definition.Limit.Value}");
        }

        return sb.ToString();
    }

    private async Task<string> BuildColumnExpressionAsync(
        ViewColumnSpec col,
        TableMetadata baseTable,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        if (col.Aggregation.HasValue)
        {
            sb.Append(MapAggregationFunction(col.Aggregation.Value));
            sb.Append('(');
        }

        if (!string.IsNullOrEmpty(col.Source))
        {
            var physicalName = await TranslateColumnNameAsync(col.Source, baseTable, cancellationToken);
            sb.Append(physicalName);
        }
        else if (!string.IsNullOrEmpty(col.Expression))
        {
            sb.Append(await TranslateExpressionAsync(col.Expression, cancellationToken));
        }
        else
        {
            throw new InvalidOperationException($"Column '{col.Alias}' must have either Source or Expression.");
        }

        if (col.Aggregation.HasValue)
        {
            sb.Append(')');
        }

        sb.Append(" AS ");
        sb.Append(DdlBuilder.QuoteIdentifier(col.Alias));

        return sb.ToString();
    }

    private async Task<string> BuildFilterConditionAsync(
        ViewFilterSpec filter,
        TableMetadata baseTable,
        CancellationToken cancellationToken)
    {
        var fieldPhysical = await TranslateColumnNameAsync(filter.Field, baseTable, cancellationToken);
        var valueExpr = FormatFilterValue(filter.Value, filter.Operator);

        return filter.Operator switch
        {
            FilterOperator.Equals => $"{fieldPhysical} = {valueExpr}",
            FilterOperator.NotEquals => $"{fieldPhysical} <> {valueExpr}",
            FilterOperator.GreaterThan => $"{fieldPhysical} > {valueExpr}",
            FilterOperator.GreaterThanOrEquals => $"{fieldPhysical} >= {valueExpr}",
            FilterOperator.LessThan => $"{fieldPhysical} < {valueExpr}",
            FilterOperator.LessThanOrEquals => $"{fieldPhysical} <= {valueExpr}",
            FilterOperator.Like => $"{fieldPhysical} LIKE {valueExpr}",
            FilterOperator.ILike => $"{fieldPhysical} ILIKE {valueExpr}",
            FilterOperator.In => $"{fieldPhysical} IN ({valueExpr})",
            FilterOperator.NotIn => $"{fieldPhysical} NOT IN ({valueExpr})",
            FilterOperator.IsNull => $"{fieldPhysical} IS NULL",
            FilterOperator.IsNotNull => $"{fieldPhysical} IS NOT NULL",
            FilterOperator.Between => $"{fieldPhysical} BETWEEN {valueExpr}",
            FilterOperator.Contains => $"{fieldPhysical} LIKE '%' || {valueExpr} || '%'",
            FilterOperator.StartsWith => $"{fieldPhysical} LIKE {valueExpr} || '%'",
            FilterOperator.EndsWith => $"{fieldPhysical} LIKE '%' || {valueExpr}",
            _ => throw new NotSupportedException($"Filter operator '{filter.Operator}' is not supported.")
        };
    }

    private static string FormatFilterValue(object? value, FilterOperator op)
    {
        if (value == null)
            return "NULL";

        if (op == FilterOperator.IsNull || op == FilterOperator.IsNotNull)
            return string.Empty;

        if (value is string strValue)
            return $"'{strValue.Replace("'", "''")}'";

        if (value is bool boolValue)
            return boolValue ? "true" : "false";

        if (value is IEnumerable<object> enumerable && op is FilterOperator.In or FilterOperator.NotIn)
        {
            var values = enumerable.Select(v => FormatFilterValue(v, FilterOperator.Equals));
            return string.Join(", ", values);
        }

        return value.ToString() ?? "NULL";
    }

    private async Task<string> TranslateColumnNameAsync(
        string logicalName,
        TableMetadata baseTable,
        CancellationToken cancellationToken)
    {
        // Check for table prefix (e.g., "orders.customer_id")
        var parts = logicalName.Split('.', 2);
        if (parts.Length == 2)
        {
            var tableName = parts[0];
            var columnName = parts[1];
            var table = await GetTableMetadataAsync(tableName, cancellationToken);
            if (table != null)
            {
                var column = table.Columns.FirstOrDefault(c => c.LogicalName == columnName);
                if (column != null)
                {
                    return $"{DdlBuilder.QuoteIdentifier(tableName)}.{DdlBuilder.QuoteIdentifier(column.PhysicalName)}";
                }
            }
            return $"{DdlBuilder.QuoteIdentifier(tableName)}.{DdlBuilder.QuoteIdentifier(columnName)}";
        }

        // Look in base table
        var baseColumn = baseTable.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        if (baseColumn != null)
        {
            return $"base_table.{DdlBuilder.QuoteIdentifier(baseColumn.PhysicalName)}";
        }

        // System columns use logical = physical
        if (logicalName.StartsWith('_'))
        {
            return $"base_table.{DdlBuilder.QuoteIdentifier(logicalName)}";
        }

        return DdlBuilder.QuoteIdentifier(logicalName);
    }

    private static Task<string> TranslateConditionAsync(string condition, CancellationToken cancellationToken)
    {
        // Simple translation - in a production system, this would be more sophisticated
        // For now, assume conditions use physical names or table.column format
        _ = cancellationToken; // Suppress unused parameter warning
        return Task.FromResult(condition);
    }

    private static Task<string> TranslateExpressionAsync(string expression, CancellationToken cancellationToken)
    {
        // Simple translation - in a production system, this would parse and translate
        _ = cancellationToken; // Suppress unused parameter warning
        return Task.FromResult(expression);
    }

    private async Task<TableMetadata?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken)
    {
        if (_tableCache.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var metadata = await _metadataRepository.GetTableByNameAsync(_tenantId, tableName, includeColumns: true, cancellationToken);
        if (metadata != null)
        {
            _tableCache[tableName] = metadata;
        }

        return metadata;
    }

    private static string MapJoinType(ViewJoinType joinType) => joinType switch
    {
        ViewJoinType.Inner => "INNER JOIN",
        ViewJoinType.Left => "LEFT JOIN",
        ViewJoinType.Right => "RIGHT JOIN",
        ViewJoinType.Full => "FULL OUTER JOIN",
        ViewJoinType.Cross => "CROSS JOIN",
        _ => "LEFT JOIN"
    };

    private static string MapAggregationFunction(AggregationFunction func) => func switch
    {
        AggregationFunction.Count => "COUNT",
        AggregationFunction.Sum => "SUM",
        AggregationFunction.Avg => "AVG",
        AggregationFunction.Min => "MIN",
        AggregationFunction.Max => "MAX",
        AggregationFunction.ArrayAgg => "ARRAY_AGG",
        AggregationFunction.StringAgg => "STRING_AGG",
        AggregationFunction.First => "FIRST_VALUE",
        AggregationFunction.Last => "LAST_VALUE",
        _ => "COUNT"
    };
}
