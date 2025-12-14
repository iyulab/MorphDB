using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;
using SqlKata.Compilers;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// PostgreSQL implementation of IMorphQueryBuilder using SqlKata.
/// Provides fluent query building with logical-to-physical name translation.
/// </summary>
public sealed class MorphQueryBuilder : IMorphQueryBuilder
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly Guid _tenantId;

    /// <summary>
    /// Creates a new MorphQueryBuilder.
    /// </summary>
    public MorphQueryBuilder(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        Guid tenantId)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _tenantId = tenantId;
    }

    /// <inheritdoc />
    public IMorphQuery From(string tableName)
    {
        return new MorphQuery(_dataSource, _metadataRepository, _tenantId, tableName, null);
    }

    /// <inheritdoc />
    public IMorphQuery From(string tableName, string tableAlias)
    {
        return new MorphQuery(_dataSource, _metadataRepository, _tenantId, tableName, tableAlias);
    }
}

/// <summary>
/// PostgreSQL implementation of IMorphQuery using SqlKata.
/// Stores query operations with logical names, then builds SQL with physical names at execution.
/// </summary>
internal sealed class MorphQuery : IMorphQuery
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly Guid _tenantId;
    private readonly string _tableName;
    private readonly string? _tableAlias;
    private readonly PostgresCompiler _compiler;

    private TableMetadata? _tableMetadata;

    // Store all query operations with logical names
    private bool _selectAllCalled;
    private readonly List<string> _selectedColumns = [];
    private readonly List<(AggregateFunction Function, string Column, string? Alias)> _aggregates = [];
    private readonly List<WhereCondition> _whereConditions = [];
    private readonly List<(string Table, string SourceColumn, string TargetColumn, bool IsLeft)> _joins = [];
    private readonly List<(string Column, bool Descending)> _orderByClauses = [];
    private readonly List<string> _groupByColumns = [];
    private readonly List<WhereCondition> _havingConditions = [];
    private int? _limit;
    private int? _offset;

    private sealed record WhereCondition(
        string Column,
        FilterOperator Operator,
        object? Value,
        bool IsOr,
        ConditionType Type);

    private enum ConditionType
    {
        Standard,
        In,
        NotIn,
        Null,
        NotNull
    }

    internal MorphQuery(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        Guid tenantId,
        string tableName,
        string? tableAlias)
    {
        _dataSource = dataSource;
        _metadataRepository = metadataRepository;
        _tenantId = tenantId;
        _tableName = tableName;
        _tableAlias = tableAlias;
        _compiler = new PostgresCompiler();
    }

    #region SELECT

    /// <inheritdoc />
    public IMorphQuery SelectColumns(params string[] columns)
    {
        foreach (var column in columns)
        {
            _selectedColumns.Add(column);
        }
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery SelectAll()
    {
        _selectAllCalled = true;
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery SelectAggregate(AggregateFunction aggregateFunction, string columnName, string? resultAlias = null)
    {
        _aggregates.Add((aggregateFunction, columnName, resultAlias));
        return this;
    }

    #endregion

    #region WHERE

    /// <inheritdoc />
    public IMorphQuery Where(string column, FilterOperator op, object? value)
    {
        _whereConditions.Add(new WhereCondition(column, op, value, false, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery AndWhere(string column, FilterOperator op, object? value)
    {
        _whereConditions.Add(new WhereCondition(column, op, value, false, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery OrWhere(string column, FilterOperator op, object? value)
    {
        _whereConditions.Add(new WhereCondition(column, op, value, true, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereIn(string column, IEnumerable<object> values)
    {
        _whereConditions.Add(new WhereCondition(column, FilterOperator.Equals, values.ToArray(), false, ConditionType.In));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNotIn(string column, IEnumerable<object> values)
    {
        _whereConditions.Add(new WhereCondition(column, FilterOperator.Equals, values.ToArray(), false, ConditionType.NotIn));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNull(string column)
    {
        _whereConditions.Add(new WhereCondition(column, FilterOperator.Equals, null, false, ConditionType.Null));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNotNull(string column)
    {
        _whereConditions.Add(new WhereCondition(column, FilterOperator.Equals, null, false, ConditionType.NotNull));
        return this;
    }

    #endregion

    #region JOIN

    /// <inheritdoc />
    public IMorphQuery Join(string tableName, string sourceColumn, string targetColumn)
    {
        _joins.Add((tableName, sourceColumn, targetColumn, false));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery LeftJoin(string tableName, string sourceColumn, string targetColumn)
    {
        _joins.Add((tableName, sourceColumn, targetColumn, true));
        return this;
    }

    #endregion

    #region ORDER BY

    /// <inheritdoc />
    public IMorphQuery OrderBy(string column)
    {
        _orderByClauses.Add((column, false));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery OrderByDesc(string column)
    {
        _orderByClauses.Add((column, true));
        return this;
    }

    #endregion

    #region GROUP BY / HAVING

    /// <inheritdoc />
    public IMorphQuery GroupBy(params string[] columns)
    {
        _groupByColumns.AddRange(columns);
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery Having(string column, FilterOperator op, object? value)
    {
        _havingConditions.Add(new WhereCondition(column, op, value, false, ConditionType.Standard));
        return this;
    }

    #endregion

    #region PAGINATION

    /// <inheritdoc />
    public IMorphQuery Limit(int count)
    {
        _limit = count;
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery Offset(int count)
    {
        _offset = count;
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery After(string cursorColumn, object cursorValue, int pageSize)
    {
        _whereConditions.Add(new WhereCondition(cursorColumn, FilterOperator.GreaterThan, cursorValue, false, ConditionType.Standard));
        _orderByClauses.Add((cursorColumn, false));
        _limit = pageSize;
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery Before(string cursorColumn, object cursorValue, int pageSize)
    {
        _whereConditions.Add(new WhereCondition(cursorColumn, FilterOperator.LessThan, cursorValue, false, ConditionType.Standard));
        _orderByClauses.Add((cursorColumn, true));
        _limit = pageSize;
        return this;
    }

    #endregion

    #region EXECUTION

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDictionary<string, object?>>> ToListAsync(
        CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = await CompileQueryAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var table = await GetTableMetadataAsync(cancellationToken);
        var mappedResults = new List<IDictionary<string, object?>>();
        foreach (var row in results)
        {
            mappedResults.Add(MapToLogicalDictionary(row, table.Columns));
        }
        return mappedResults;
    }

    /// <inheritdoc />
    public async Task<IDictionary<string, object?>?> FirstOrDefaultAsync(
        CancellationToken cancellationToken = default)
    {
        _limit = 1;
        var results = await ToListAsync(cancellationToken);
        return results.Count > 0 ? results[0] : null;
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var countQuery = query.AsCount();
        var compiled = _compiler.Compile(countQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal?> SumAsync(string column, CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var physicalColumn = GetPhysicalColumnName(column, table);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var sumQuery = query.AsSum(physicalColumn);
        var compiled = _compiler.Compile(sumQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal?> AvgAsync(string column, CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var physicalColumn = GetPhysicalColumnName(column, table);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var avgQuery = query.AsAverage(physicalColumn);
        var compiled = _compiler.Compile(avgQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<T?> MinAsync<T>(string column, CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var physicalColumn = GetPhysicalColumnName(column, table);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var minQuery = query.AsMin(physicalColumn);
        var compiled = _compiler.Compile(minQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<T?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<T?> MaxAsync<T>(string column, CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var physicalColumn = GetPhysicalColumnName(column, table);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var maxQuery = query.AsMax(physicalColumn);
        var compiled = _compiler.Compile(maxQuery);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<T?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    #endregion

    #region SQL Generation

    /// <inheritdoc />
    public string ToSql()
    {
        // For debugging, use logical names since we may not have metadata
        var query = BuildLogicalQuery();
        var compiled = _compiler.Compile(query);
        return compiled.Sql;
    }

    /// <inheritdoc />
    public IDictionary<string, object?> GetParameters()
    {
        var query = BuildLogicalQuery();
        var compiled = _compiler.Compile(query);
        return compiled.NamedBindings.ToDictionary(
            kvp => kvp.Key,
            kvp => (object?)kvp.Value);
    }

    /// <summary>
    /// Builds a SqlKata query using logical names (for debugging/ToSql).
    /// </summary>
    private SqlKataQuery BuildLogicalQuery()
    {
        var query = new SqlKataQuery(_tableName);

        if (!string.IsNullOrEmpty(_tableAlias))
        {
            query.As(_tableAlias);
        }

        // SELECT
        if (_selectAllCalled || (_selectedColumns.Count == 0 && _aggregates.Count == 0))
        {
            query.Select("*");
        }
        else if (_selectedColumns.Count > 0)
        {
            query.Select(_selectedColumns.ToArray());
        }

        // Aggregates
        foreach (var (function, column, alias) in _aggregates)
        {
            var aggExpr = BuildAggregateExpression(function, column);
            if (!string.IsNullOrEmpty(alias))
            {
                query.SelectRaw($"{aggExpr} AS {alias}");
            }
            else
            {
                query.SelectRaw(aggExpr);
            }
        }

        // WHERE
        ApplyWhereConditions(query, _whereConditions);

        // JOIN (not transforming for debug output)
        foreach (var (table, source, target, isLeft) in _joins)
        {
            if (isLeft)
            {
                query.LeftJoin(table, source, target);
            }
            else
            {
                query.Join(table, source, target);
            }
        }

        // ORDER BY
        foreach (var (column, descending) in _orderByClauses)
        {
            if (descending)
            {
                query.OrderByDesc(column);
            }
            else
            {
                query.OrderBy(column);
            }
        }

        // GROUP BY
        if (_groupByColumns.Count > 0)
        {
            query.GroupBy(_groupByColumns.ToArray());
        }

        // HAVING
        foreach (var condition in _havingConditions)
        {
            var (sqlOp, value) = GetSqlOperator(condition.Operator, condition.Value);
            query.HavingRaw($"{condition.Column} {sqlOp} ?", value);
        }

        // LIMIT/OFFSET
        if (_limit.HasValue)
        {
            query.Limit(_limit.Value);
        }

        if (_offset.HasValue)
        {
            query.Offset(_offset.Value);
        }

        return query;
    }

    /// <summary>
    /// Builds a SqlKata query with physical names for actual execution.
    /// </summary>
    private async Task<SqlKataQuery> BuildPhysicalQueryAsync(CancellationToken cancellationToken)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var query = new SqlKataQuery(table.PhysicalName);

        if (!string.IsNullOrEmpty(_tableAlias))
        {
            query.As(_tableAlias);
        }

        // SELECT - Note: We don't add SELECT here for aggregate queries
        // The calling method (CountAsync, SumAsync, etc.) will handle the aggregate

        // WHERE - Transform logical column names to physical
        ApplyPhysicalWhereConditions(query, _whereConditions, table);

        // JOIN - would need to resolve joined table metadata too
        // For now, joins are not fully supported with name translation
        foreach (var (joinTable, source, target, isLeft) in _joins)
        {
            // TODO: Resolve joined table's physical name
            if (isLeft)
            {
                query.LeftJoin(joinTable, source, target);
            }
            else
            {
                query.Join(joinTable, source, target);
            }
        }

        // ORDER BY
        foreach (var (column, descending) in _orderByClauses)
        {
            var physicalColumn = GetPhysicalColumnName(column, table);
            if (descending)
            {
                query.OrderByDesc(physicalColumn);
            }
            else
            {
                query.OrderBy(physicalColumn);
            }
        }

        // GROUP BY
        if (_groupByColumns.Count > 0)
        {
            var physicalGroupBy = _groupByColumns
                .Select(c => GetPhysicalColumnName(c, table))
                .ToArray();
            query.GroupBy(physicalGroupBy);
        }

        // HAVING
        foreach (var condition in _havingConditions)
        {
            var physicalColumn = GetPhysicalColumnName(condition.Column, table);
            var (sqlOp, value) = GetSqlOperator(condition.Operator, condition.Value);
            query.HavingRaw($"{physicalColumn} {sqlOp} ?", value);
        }

        // LIMIT/OFFSET
        if (_limit.HasValue)
        {
            query.Limit(_limit.Value);
        }

        if (_offset.HasValue)
        {
            query.Offset(_offset.Value);
        }

        return query;
    }

    private async Task<(string Sql, object Parameters)> CompileQueryAsync(
        CancellationToken cancellationToken)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var query = await BuildPhysicalQueryAsync(cancellationToken);

        // Add SELECT clause
        if (_selectAllCalled || (_selectedColumns.Count == 0 && _aggregates.Count == 0))
        {
            query.Select("*");
        }
        else if (_selectedColumns.Count > 0)
        {
            var physicalColumns = _selectedColumns
                .Select(c => GetPhysicalColumnName(c, table))
                .ToArray();
            query.Select(physicalColumns);
        }

        // Aggregates
        foreach (var (function, column, alias) in _aggregates)
        {
            var physicalColumn = GetPhysicalColumnName(column, table);
            var aggExpr = BuildAggregateExpression(function, physicalColumn);
            if (!string.IsNullOrEmpty(alias))
            {
                query.SelectRaw($"{aggExpr} AS {alias}");
            }
            else
            {
                query.SelectRaw(aggExpr);
            }
        }

        var compiled = _compiler.Compile(query);
        return (compiled.Sql, compiled.NamedBindings);
    }

    private static void ApplyWhereConditions(SqlKataQuery query, List<WhereCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            ApplyWhereCondition(query, condition.Column, condition);
        }
    }

    private static void ApplyPhysicalWhereConditions(
        SqlKataQuery query,
        List<WhereCondition> conditions,
        TableMetadata table)
    {
        foreach (var condition in conditions)
        {
            var physicalColumn = GetPhysicalColumnName(condition.Column, table);
            ApplyWhereCondition(query, physicalColumn, condition);
        }
    }

    private static void ApplyWhereCondition(SqlKataQuery query, string column, WhereCondition condition)
    {
        switch (condition.Type)
        {
            case ConditionType.In:
                if (condition.Value is object[] inValues)
                {
                    query.WhereIn(column, inValues);
                }
                break;

            case ConditionType.NotIn:
                if (condition.Value is object[] notInValues)
                {
                    query.WhereNotIn(column, notInValues);
                }
                break;

            case ConditionType.Null:
                query.WhereNull(column);
                break;

            case ConditionType.NotNull:
                query.WhereNotNull(column);
                break;

            case ConditionType.Standard:
                ApplyStandardWhereCondition(query, column, condition.Operator, condition.Value, condition.IsOr);
                break;
        }
    }

    private static void ApplyStandardWhereCondition(
        SqlKataQuery query,
        string column,
        FilterOperator op,
        object? value,
        bool isOr)
    {
        switch (op)
        {
            case FilterOperator.Equals:
                if (isOr)
                    query.OrWhere(column, "=", value);
                else
                    query.Where(column, "=", value);
                break;
            case FilterOperator.NotEquals:
                if (isOr)
                    query.OrWhere(column, "!=", value);
                else
                    query.Where(column, "!=", value);
                break;
            case FilterOperator.GreaterThan:
                if (isOr)
                    query.OrWhere(column, ">", value);
                else
                    query.Where(column, ">", value);
                break;
            case FilterOperator.GreaterThanOrEquals:
                if (isOr)
                    query.OrWhere(column, ">=", value);
                else
                    query.Where(column, ">=", value);
                break;
            case FilterOperator.LessThan:
                if (isOr)
                    query.OrWhere(column, "<", value);
                else
                    query.Where(column, "<", value);
                break;
            case FilterOperator.LessThanOrEquals:
                if (isOr)
                    query.OrWhere(column, "<=", value);
                else
                    query.Where(column, "<=", value);
                break;
            case FilterOperator.Like:
                if (isOr)
                    query.OrWhereLike(column, value?.ToString() ?? "", caseSensitive: true);
                else
                    query.WhereLike(column, value?.ToString() ?? "", caseSensitive: true);
                break;
            case FilterOperator.NotLike:
                if (isOr)
                    query.OrWhereNotLike(column, value?.ToString() ?? "", caseSensitive: true);
                else
                    query.WhereNotLike(column, value?.ToString() ?? "", caseSensitive: true);
                break;
            case FilterOperator.ILike:
                if (isOr)
                    query.OrWhereLike(column, value?.ToString() ?? "", caseSensitive: false);
                else
                    query.WhereLike(column, value?.ToString() ?? "", caseSensitive: false);
                break;
            case FilterOperator.Contains:
                var containsPattern = $"%{value}%";
                if (isOr)
                    query.OrWhereLike(column, containsPattern, caseSensitive: false);
                else
                    query.WhereLike(column, containsPattern, caseSensitive: false);
                break;
            case FilterOperator.StartsWith:
                var startsWithPattern = $"{value}%";
                if (isOr)
                    query.OrWhereLike(column, startsWithPattern, caseSensitive: false);
                else
                    query.WhereLike(column, startsWithPattern, caseSensitive: false);
                break;
            case FilterOperator.EndsWith:
                var endsWithPattern = $"%{value}";
                if (isOr)
                    query.OrWhereLike(column, endsWithPattern, caseSensitive: false);
                else
                    query.WhereLike(column, endsWithPattern, caseSensitive: false);
                break;
        }
    }

    private static string BuildAggregateExpression(AggregateFunction function, string column)
    {
        return function switch
        {
            AggregateFunction.Count => $"COUNT({column})",
            AggregateFunction.CountDistinct => $"COUNT(DISTINCT {column})",
            AggregateFunction.Sum => $"SUM({column})",
            AggregateFunction.Avg => $"AVG({column})",
            AggregateFunction.Min => $"MIN({column})",
            AggregateFunction.Max => $"MAX({column})",
            _ => column
        };
    }

    private static (string Op, object? Value) GetSqlOperator(FilterOperator op, object? value)
    {
        return op switch
        {
            FilterOperator.Equals => ("=", value),
            FilterOperator.NotEquals => ("!=", value),
            FilterOperator.GreaterThan => (">", value),
            FilterOperator.GreaterThanOrEquals => (">=", value),
            FilterOperator.LessThan => ("<", value),
            FilterOperator.LessThanOrEquals => ("<=", value),
            FilterOperator.Like => ("LIKE", value),
            FilterOperator.NotLike => ("NOT LIKE", value),
            FilterOperator.ILike => ("ILIKE", value),
            FilterOperator.Contains => ("ILIKE", $"%{value}%"),
            FilterOperator.StartsWith => ("ILIKE", $"{value}%"),
            FilterOperator.EndsWith => ("ILIKE", $"%{value}"),
            _ => ("=", value)
        };
    }

    private static string GetPhysicalColumnName(string logicalName, TableMetadata table)
    {
        var column = table.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        return column?.PhysicalName ?? logicalName;
    }

    private async Task<TableMetadata> GetTableMetadataAsync(CancellationToken cancellationToken)
    {
        if (_tableMetadata is not null)
            return _tableMetadata;

        _tableMetadata = await _metadataRepository.GetTableByNameAsync(
            _tenantId, _tableName, includeColumns: true, cancellationToken);

        if (_tableMetadata is null)
            throw new InvalidOperationException($"Table '{_tableName}' not found for tenant '{_tenantId}'");

        return _tableMetadata;
    }

    private static Dictionary<string, object?> MapToLogicalDictionary(
        dynamic row,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var physicalToLogical = columns.ToDictionary(
            c => c.PhysicalName.ToLowerInvariant(),
            c => c);
        var result = new Dictionary<string, object?>();

        var rowDict = (IDictionary<string, object?>)row;

        foreach (var (key, value) in rowDict)
        {
            var normalizedKey = key.ToLowerInvariant();
            if (physicalToLogical.TryGetValue(normalizedKey, out var column))
            {
                var convertedValue = TypeMapper.FromDbValue(value, column.DataType);
                result[column.LogicalName] = convertedValue;
            }
            else
            {
                // Unknown column (e.g., aggregate alias), keep as-is
                result[key] = value;
            }
        }

        return result;
    }

    #endregion
}
