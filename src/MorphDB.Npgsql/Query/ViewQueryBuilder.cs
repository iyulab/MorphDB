using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// Builds SQL SELECT statements from view definitions.
/// Handles logical-to-physical name translation for views.
/// </summary>
public sealed class ViewQueryBuilder
{
    private readonly IMetadataRepository _metadataRepository;
    private readonly Guid _projectId;
    private readonly Dictionary<string, TableMetadata> _tableCache = new();

    public ViewQueryBuilder(IMetadataRepository metadataRepository, Guid projectId)
    {
        _metadataRepository = metadataRepository;
        _projectId = projectId;
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
            throw new MorphDB.Core.Exceptions.TableNotFoundException(definition.BaseTable);
        }

        // Maps each join's *logical* table name to the SQL qualifier it is actually reachable
        // under in this statement's FROM/JOIN clauses -- "base_table" for the base table, the
        // declared alias for an aliased join, or the join table's physical name when unaliased
        // (matching exactly what the FROM/JOIN loop below emits). Built up front so a condition's
        // "orders.customer_id" resolves to the same table the query can actually see, rather than
        // a logical name no FROM/JOIN clause ever introduces.
        var tableQualifiers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [definition.BaseTable] = "base_table"
        };
        foreach (var join in definition.Joins)
        {
            var joinTable = await GetTableMetadataAsync(join.Table, cancellationToken);
            if (joinTable != null)
            {
                tableQualifiers[join.Table] = !string.IsNullOrEmpty(join.Alias)
                    ? DdlBuilder.QuoteIdentifier(join.Alias)
                    : DdlBuilder.QuoteIdentifier(joinTable.PhysicalName);
            }
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
                columnExpressions.Add(await BuildColumnExpressionAsync(col, baseTable, tableQualifiers, cancellationToken));
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
                throw new MorphDB.Core.Exceptions.TableNotFoundException(join.Table);
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
            sb.Append(await TranslateConditionAsync(join.Condition, baseTable, tableQualifiers, cancellationToken));
        }

        // Build WHERE clause
        if (definition.Filters.Count > 0)
        {
            sb.Append("\nWHERE ");
            var filterConditions = new List<string>();
            for (int i = 0; i < definition.Filters.Count; i++)
            {
                var filter = definition.Filters[i];
                var condition = await BuildFilterConditionAsync(filter, baseTable, tableQualifiers, cancellationToken);
                if (i > 0)
                {
                    sb.Append(filter.LogicalOp == LogicalOperator.And ? " AND " : " OR ");
                }
                filterConditions.Add(condition);
            }
            sb.Append(string.Join("", filterConditions.Select((c, i) => i == 0 ? c : c)));
        }

        // Add project isolation
        var projectCondition = $"base_table.\"project_id\" = '{_projectId}'";
        if (definition.Filters.Count > 0)
        {
            sb.Append(" AND ");
            sb.Append(projectCondition);
        }
        else
        {
            sb.Append("\nWHERE ");
            sb.Append(projectCondition);
        }

        // Build GROUP BY clause
        if (definition.GroupBy.Count > 0)
        {
            sb.Append("\nGROUP BY ");
            var groupByColumns = new List<string>();
            foreach (var col in definition.GroupBy)
            {
                var physicalName = await TranslateColumnNameAsync(col, baseTable, tableQualifiers, cancellationToken);
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
                var physicalName = await TranslateColumnNameAsync(order.Column, baseTable, tableQualifiers, cancellationToken);
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
        IReadOnlyDictionary<string, string> tableQualifiers,
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
            var physicalName = await TranslateColumnNameAsync(col.Source, baseTable, tableQualifiers, cancellationToken);
            sb.Append(physicalName);
        }
        else if (!string.IsNullOrEmpty(col.Expression))
        {
            sb.Append(await TranslateExpressionAsync(col.Expression, baseTable, tableQualifiers, cancellationToken));
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
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken)
    {
        var fieldPhysical = await TranslateColumnNameAsync(filter.Field, baseTable, tableQualifiers, cancellationToken);
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
        // Filter values cross the API boundary (and JSONB storage) as JsonElement; unwrap so the
        // type checks below quote/render them correctly instead of falling through to ToString().
        value = JsonValueConverter.ToClrValue(value);

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
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken)
    {
        // Check for table prefix (e.g., "orders.customer_id")
        var parts = logicalName.Split('.', 2);
        if (parts.Length == 2)
        {
            var tableName = parts[0];
            var columnName = parts[1];
            var qualifier = tableQualifiers.TryGetValue(tableName, out var mapped)
                ? mapped
                : DdlBuilder.QuoteIdentifier(tableName);
            var table = await GetTableMetadataAsync(tableName, cancellationToken);
            if (table != null)
            {
                var column = table.Columns.FirstOrDefault(c => c.LogicalName == columnName);
                if (column != null)
                {
                    return $"{qualifier}.{DdlBuilder.QuoteIdentifier(column.PhysicalName)}";
                }
            }
            return $"{qualifier}.{DdlBuilder.QuoteIdentifier(columnName)}";
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

    private Task<string> TranslateConditionAsync(
        string condition,
        TableMetadata baseTable,
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken) =>
        TranslateIdentifiersAsync(condition, baseTable, tableQualifiers, cancellationToken);

    private Task<string> TranslateExpressionAsync(
        string expression,
        TableMetadata baseTable,
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken) =>
        TranslateIdentifiersAsync(expression, baseTable, tableQualifiers, cancellationToken);

    // Matches either a single-quoted SQL string literal (copied verbatim, never treated as a
    // column reference) or a bare/dotted identifier ("customer_id", "orders.customer_id").
    private static readonly Regex IdentifierOrStringLiteral = new(
        @"'(?:[^']|'')*'|\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Translates logical column references embedded in a free-form condition or expression
    /// string (e.g. "orders.customer_id = customers._id", "price * quantity") to their physical
    /// equivalents, leaving everything else (operators, literals, SQL keywords, function names)
    /// untouched. Unlike <see cref="TranslateColumnNameAsync"/> -- which is only ever called with
    /// text already known to be a column reference (Source/GroupBy/OrderBy) and so can safely
    /// quote-and-assume on a miss -- a token here that fails to resolve is left as-is, since it
    /// may well be a keyword or function name rather than an unknown column.
    /// </summary>
    private async Task<string> TranslateIdentifiersAsync(
        string text,
        TableMetadata baseTable,
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in IdentifierOrStringLiteral.Matches(text))
        {
            result.Append(text, lastIndex, match.Index - lastIndex);

            result.Append(match.Value.StartsWith('\'')
                ? match.Value
                : await TryTranslateColumnReferenceAsync(match.Value, baseTable, tableQualifiers, cancellationToken));

            lastIndex = match.Index + match.Length;
        }

        result.Append(text, lastIndex, text.Length - lastIndex);
        return result.ToString();
    }

    private async Task<string> TryTranslateColumnReferenceAsync(
        string token,
        TableMetadata baseTable,
        IReadOnlyDictionary<string, string> tableQualifiers,
        CancellationToken cancellationToken)
    {
        var parts = token.Split('.', 2);
        if (parts.Length == 2)
        {
            var table = await GetTableMetadataAsync(parts[0], cancellationToken);
            var column = table?.Columns.FirstOrDefault(c => c.LogicalName == parts[1]);
            if (column == null)
            {
                return token;
            }

            var qualifier = tableQualifiers.TryGetValue(parts[0], out var mapped)
                ? mapped
                : DdlBuilder.QuoteIdentifier(parts[0]);
            return $"{qualifier}.{DdlBuilder.QuoteIdentifier(column.PhysicalName)}";
        }

        var baseColumn = baseTable.Columns.FirstOrDefault(c => c.LogicalName == token);
        if (baseColumn != null)
        {
            return $"base_table.{DdlBuilder.QuoteIdentifier(baseColumn.PhysicalName)}";
        }

        // System columns use logical = physical, same rule TranslateColumnNameAsync applies.
        return token.StartsWith('_') ? $"base_table.{DdlBuilder.QuoteIdentifier(token)}" : token;
    }

    private async Task<TableMetadata?> GetTableMetadataAsync(string tableName, CancellationToken cancellationToken)
    {
        if (_tableCache.TryGetValue(tableName, out var cached))
        {
            return cached;
        }

        var metadata = await _metadataRepository.GetTableByNameAsync(_projectId, tableName, includeColumns: true, cancellationToken);
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
