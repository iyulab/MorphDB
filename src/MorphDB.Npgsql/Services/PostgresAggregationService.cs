using System.Diagnostics;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Repositories;
using Npgsql;
using SqlKata.Compilers;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of IAggregationService.
/// Provides server-side aggregation queries with GROUP BY support.
/// </summary>
public sealed class PostgresAggregationService : IAggregationService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly PostgresCompiler _compiler;

    /// <summary>
    /// Creates a new PostgresAggregationService.
    /// </summary>
    public PostgresAggregationService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _securityPolicyService = securityPolicyService ?? throw new ArgumentNullException(nameof(securityPolicyService));
        _securityContextAccessor = securityContextAccessor ?? throw new ArgumentNullException(nameof(securityContextAccessor));
        _compiler = new PostgresCompiler();
    }

    /// <inheritdoc />
    public async Task<AggregationResult> AggregateAsync(
        Guid projectId,
        string tableName,
        AggregationRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get table metadata with columns
        var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, includeColumns: true, cancellationToken)
            ?? throw new TableNotFoundException(tableName);

        // Build the aggregation query
        var query = BuildAggregationQuery(table, request);

        // Apply Row-Level Security
        await ApplyRlsPolicyAsync(query, projectId, tableName, cancellationToken);

        // Compile and execute
        var compiled = _compiler.Compile(query);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        // Map results to logical names
        var data = results
            .Select(row => (IDictionary<string, object?>)MapToLogicalDictionary(row, request))
            .ToList();

        stopwatch.Stop();

        // Calculate total groups if limit/offset is applied
        long? totalGroups = null;
        if (request.Limit.HasValue || request.Offset.HasValue)
        {
            totalGroups = await CountGroupsAsync(projectId, table, tableName, request, cancellationToken);
        }

        return new AggregationResult
        {
            Data = data,
            TotalGroups = totalGroups,
            Metadata = new AggregationMetadata
            {
                RowsScanned = data.Count,
                ExecutionTimeMs = stopwatch.Elapsed.TotalMilliseconds
            }
        };
    }

    private static SqlKataQuery BuildAggregationQuery(TableMetadata table, AggregationRequest request)
    {
        var query = new SqlKataQuery(table.PhysicalName);

        // Add GROUP BY columns to SELECT
        foreach (var groupColumn in request.GroupBy)
        {
            var column = table.Columns.FirstOrDefault(c => c.LogicalName == groupColumn)
                ?? throw new ColumnNotFoundException(table.LogicalName, groupColumn);

            query.Select($"{table.PhysicalName}.{column.PhysicalName} AS {groupColumn}");
        }

        // Add aggregation columns to SELECT
        foreach (var agg in request.Aggregations)
        {
            var aggSql = BuildAggregationSql(table, agg);
            query.SelectRaw($"{aggSql} AS {agg.Alias}");
        }

        // Add WHERE conditions
        if (request.Filter is { Count: > 0 })
        {
            ApplyFilterConditions(query, table, request.Filter);
        }

        // Add GROUP BY
        foreach (var groupColumn in request.GroupBy)
        {
            var column = table.Columns.First(c => c.LogicalName == groupColumn);
            query.GroupBy($"{table.PhysicalName}.{column.PhysicalName}");
        }

        // Add HAVING conditions
        if (request.Having is { Count: > 0 })
        {
            ApplyHavingConditions(query, table, request);
        }

        // Add ORDER BY
        if (request.OrderBy is { Count: > 0 })
        {
            ApplyOrderBy(query, table, request);
        }

        // Add LIMIT/OFFSET
        if (request.Limit.HasValue)
        {
            query.Limit(request.Limit.Value);
        }

        if (request.Offset.HasValue)
        {
            query.Offset(request.Offset.Value);
        }

        return query;
    }

    private static string BuildAggregationSql(TableMetadata table, AggregationColumn agg)
    {
        var columnExpr = "*";

        if (agg.Column is not null)
        {
            var column = table.Columns.FirstOrDefault(c => c.LogicalName == agg.Column)
                ?? throw new ColumnNotFoundException(table.LogicalName, agg.Column);

            columnExpr = agg.Distinct
                ? $"DISTINCT {table.PhysicalName}.{column.PhysicalName}"
                : $"{table.PhysicalName}.{column.PhysicalName}";
        }

        return agg.Function switch
        {
            AggregateFunction.Count => $"COUNT({columnExpr})",
            AggregateFunction.CountDistinct => $"COUNT(DISTINCT {columnExpr})",
            AggregateFunction.Sum => $"SUM({columnExpr})",
            AggregateFunction.Avg => $"AVG({columnExpr})",
            AggregateFunction.Min => $"MIN({columnExpr})",
            AggregateFunction.Max => $"MAX({columnExpr})",
            _ => throw new ArgumentException($"Unsupported aggregate function: {agg.Function}")
        };
    }

    private static void ApplyFilterConditions(SqlKataQuery query, TableMetadata table, IReadOnlyList<FilterCondition> filters)
    {
        foreach (var filter in filters)
        {
            var column = table.Columns.FirstOrDefault(c => c.LogicalName == filter.Column)
                ?? throw new ColumnNotFoundException(table.LogicalName, filter.Column);

            var physicalColumn = $"{table.PhysicalName}.{column.PhysicalName}";

            ApplyFilter(query, physicalColumn, filter.Operator, filter.Value);
        }
    }

    private static void ApplyFilter(SqlKataQuery query, string column, FilterOperator op, object? value)
    {
        switch (op)
        {
            case FilterOperator.Equals:
                query.Where(column, value);
                break;
            case FilterOperator.NotEquals:
                query.WhereNot(column, value);
                break;
            case FilterOperator.GreaterThan:
                query.Where(column, ">", value);
                break;
            case FilterOperator.GreaterThanOrEquals:
                query.Where(column, ">=", value);
                break;
            case FilterOperator.LessThan:
                query.Where(column, "<", value);
                break;
            case FilterOperator.LessThanOrEquals:
                query.Where(column, "<=", value);
                break;
            case FilterOperator.Like:
                query.WhereLike(column, value?.ToString() ?? "");
                break;
            case FilterOperator.ILike:
                query.WhereRaw($"LOWER({column}) LIKE LOWER(?)", value?.ToString() ?? "");
                break;
            case FilterOperator.Contains:
                query.WhereLike(column, $"%{value}%");
                break;
            case FilterOperator.StartsWith:
                query.WhereLike(column, $"{value}%");
                break;
            case FilterOperator.EndsWith:
                query.WhereLike(column, $"%{value}");
                break;
            case FilterOperator.In:
                if (value is IEnumerable<object> inValues)
                    query.WhereIn(column, inValues);
                break;
            case FilterOperator.NotIn:
                if (value is IEnumerable<object> notInValues)
                    query.WhereNotIn(column, notInValues);
                break;
            case FilterOperator.IsNull:
                query.WhereNull(column);
                break;
            case FilterOperator.IsNotNull:
                query.WhereNotNull(column);
                break;
            case FilterOperator.Between:
                if (value is object[] range && range.Length == 2)
                    query.WhereBetween(column, range[0], range[1]);
                break;
            default:
                throw new ArgumentException($"Unsupported filter operator: {op}");
        }
    }

    private static void ApplyHavingConditions(SqlKataQuery query, TableMetadata table, AggregationRequest request)
    {
        foreach (var having in request.Having!)
        {
            // Find the aggregation that matches this alias
            var agg = request.Aggregations.FirstOrDefault(a => a.Alias == having.Alias);
            if (agg is null)
            {
                throw new ArgumentException($"Having condition references unknown aggregation alias: {having.Alias}");
            }

            var op = having.Operator switch
            {
                FilterOperator.Equals => "=",
                FilterOperator.NotEquals => "!=",
                FilterOperator.GreaterThan => ">",
                FilterOperator.GreaterThanOrEquals => ">=",
                FilterOperator.LessThan => "<",
                FilterOperator.LessThanOrEquals => "<=",
                _ => throw new ArgumentException($"Unsupported having operator: {having.Operator}")
            };

            // Use the full aggregate expression, not the alias (PostgreSQL doesn't allow aliases in HAVING)
            var aggSql = BuildAggregationSql(table, agg);
            query.HavingRaw($"{aggSql} {op} ?", having.Value);
        }
    }

    private static void ApplyOrderBy(SqlKataQuery query, TableMetadata table, AggregationRequest request)
    {
        foreach (var orderBy in request.OrderBy!)
        {
            // Check if ordering by an aggregation alias
            var isAggAlias = request.Aggregations.Any(a => a.Alias == orderBy.Column);

            if (isAggAlias)
            {
                // Order by aggregation alias
                if (orderBy.Descending)
                    query.OrderByRaw($"{orderBy.Column} DESC");
                else
                    query.OrderByRaw($"{orderBy.Column} ASC");
            }
            else
            {
                // Order by GROUP BY column
                var column = table.Columns.FirstOrDefault(c => c.LogicalName == orderBy.Column)
                    ?? throw new ColumnNotFoundException(table.LogicalName, orderBy.Column);

                var physicalColumn = $"{table.PhysicalName}.{column.PhysicalName}";

                if (orderBy.Descending)
                    query.OrderByDesc(physicalColumn);
                else
                    query.OrderBy(physicalColumn);
            }
        }
    }

    private async Task ApplyRlsPolicyAsync(
        SqlKataQuery query,
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var securityContext = _securityContextAccessor.ContextOrNull;
        if (securityContext is null || securityContext.BypassRls)
            return;

        var rlsExpression = await _securityPolicyService.EvaluatePoliciesAsync(
            projectId,
            tableName,
            PolicyType.Select,
            securityContext,
            cancellationToken);

        if (!string.IsNullOrEmpty(rlsExpression))
        {
            query.WhereRaw(rlsExpression);
        }
    }

    private async Task<long> CountGroupsAsync(
        Guid projectId,
        TableMetadata table,
        string tableName,
        AggregationRequest request,
        CancellationToken cancellationToken)
    {
        // Build a count query for total groups
        var subQuery = new SqlKataQuery(table.PhysicalName);

        // Add WHERE conditions
        if (request.Filter is { Count: > 0 })
        {
            ApplyFilterConditions(subQuery, table, request.Filter);
        }

        // Apply RLS
        await ApplyRlsPolicyAsync(subQuery, projectId, tableName, cancellationToken);

        // Add GROUP BY
        foreach (var groupColumn in request.GroupBy)
        {
            var column = table.Columns.First(c => c.LogicalName == groupColumn);
            subQuery.GroupBy($"{table.PhysicalName}.{column.PhysicalName}");
            subQuery.Select($"{table.PhysicalName}.{column.PhysicalName}");
        }

        // If no GROUP BY, count is 1 (single result row)
        if (request.GroupBy.Count == 0)
        {
            return 1;
        }

        // Wrap in count query
        var subQueryCompiled = _compiler.Compile(subQuery);
        var countQuery = new SqlKataQuery().FromRaw($"({subQueryCompiled.Sql}) AS grouped", subQueryCompiled.Bindings.ToArray());
        countQuery.AsCount();

        var compiled = _compiler.Compile(countQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));
    }

    private static Dictionary<string, object?> MapToLogicalDictionary(
        dynamic row,
        AggregationRequest request)
    {
        var result = new Dictionary<string, object?>();
        var rowDict = (IDictionary<string, object?>)row;

        // Map GROUP BY columns
        foreach (var groupColumn in request.GroupBy)
        {
            if (rowDict.TryGetValue(groupColumn, out var value))
            {
                result[groupColumn] = value;
            }
        }

        // Map aggregation results
        foreach (var agg in request.Aggregations)
        {
            if (rowDict.TryGetValue(agg.Alias, out var value))
            {
                result[agg.Alias] = value;
            }
        }

        return result;
    }
}
