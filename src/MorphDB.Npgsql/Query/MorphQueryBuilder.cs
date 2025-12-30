using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Security;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;
using SqlKata.Compilers;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// PostgreSQL implementation of IMorphQueryBuilder using SqlKata.
/// Provides fluent query building with logical-to-physical name translation
/// and Row-Level Security (RLS) policy enforcement.
/// </summary>
public sealed class MorphQueryBuilder : IMorphQueryBuilder
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly Guid _tenantId;

    /// <summary>
    /// Creates a new MorphQueryBuilder with security support.
    /// </summary>
    public MorphQueryBuilder(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository,
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor,
        Guid tenantId,
        ILookupResolver? lookupResolver = null,
        IRollupResolver? rollupResolver = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _securityPolicyService = securityPolicyService ?? throw new ArgumentNullException(nameof(securityPolicyService));
        _securityContextAccessor = securityContextAccessor ?? throw new ArgumentNullException(nameof(securityContextAccessor));
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _tenantId = tenantId;
    }

    /// <inheritdoc />
    public IMorphQuery From(string tableName)
    {
        return new MorphQuery(
            _dataSource,
            _metadataRepository,
            _securityPolicyService,
            _securityContextAccessor,
            _lookupResolver,
            _rollupResolver,
            _tenantId,
            tableName,
            null);
    }

    /// <inheritdoc />
    public IMorphQuery From(string tableName, string tableAlias)
    {
        return new MorphQuery(
            _dataSource,
            _metadataRepository,
            _securityPolicyService,
            _securityContextAccessor,
            _lookupResolver,
            _rollupResolver,
            _tenantId,
            tableName,
            tableAlias);
    }
}

/// <summary>
/// PostgreSQL implementation of IMorphQuery using SqlKata.
/// Stores query operations with logical names, then builds SQL with physical names at execution.
/// Automatically applies Row-Level Security (RLS) policies during query execution.
/// Supports automatic lookup column expansion via JOINs.
/// Supports automatic rollup column expansion via correlated subqueries.
/// </summary>
internal sealed class MorphQuery : IMorphQuery
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ISecurityPolicyService _securityPolicyService;
    private readonly ISecurityContextAccessor _securityContextAccessor;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly Guid _tenantId;
    private readonly string _tableName;
    private readonly string? _tableAlias;
    private readonly PostgresCompiler _compiler;

    private TableMetadata? _tableMetadata;
    private string? _rlsExpression;
    private readonly Dictionary<string, TableMetadata> _joinedTableMetadata = new();
    private LookupQueryExpansion? _lookupExpansion;
    private RollupQueryExpansion? _rollupExpansion;

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
        ISecurityPolicyService securityPolicyService,
        ISecurityContextAccessor securityContextAccessor,
        ILookupResolver? lookupResolver,
        IRollupResolver? rollupResolver,
        Guid tenantId,
        string tableName,
        string? tableAlias)
    {
        _dataSource = dataSource;
        _metadataRepository = metadataRepository;
        _securityPolicyService = securityPolicyService;
        _securityContextAccessor = securityContextAccessor;
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
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

    /// <inheritdoc />
    public async Task<(string WhereSql, IDictionary<string, object?> Parameters)> GetPhysicalWhereClauseAsync(
        CancellationToken cancellationToken = default)
    {
        if (_whereConditions.Count == 0)
        {
            return ("", new Dictionary<string, object?>());
        }

        var table = await GetTableMetadataAsync(cancellationToken);

        // Build a query with just the WHERE clause to extract it
        var query = new SqlKataQuery(table.PhysicalName);
        ApplyPhysicalWhereConditions(query, _whereConditions, table);

        var compiled = _compiler.Compile(query);

        // Extract WHERE clause from compiled SQL (format: "SELECT * FROM table WHERE ...")
        var sql = compiled.Sql;
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex < 0)
        {
            return ("", new Dictionary<string, object?>());
        }

        // Extract everything after "WHERE "
        var whereSql = sql[(whereIndex + 6)..].Trim();

        var parameters = compiled.NamedBindings.ToDictionary(
            kvp => kvp.Key,
            kvp => (object?)kvp.Value);

        return (whereSql, parameters);
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
    /// Uses "base_table" as alias when lookup or rollup expansion is present.
    /// </summary>
    private async Task<SqlKataQuery> BuildPhysicalQueryAsync(CancellationToken cancellationToken)
    {
        var table = await GetTableMetadataAsync(cancellationToken);
        var query = new SqlKataQuery(table.PhysicalName);

        // Use base_table alias when lookup or rollup expansion is present
        var useBaseTableAlias = _lookupExpansion?.HasExpansion == true || _rollupExpansion?.HasExpansion == true;
        if (useBaseTableAlias)
        {
            query.As("base_table");
        }
        else if (!string.IsNullOrEmpty(_tableAlias))
        {
            query.As(_tableAlias);
        }

        // SELECT - Note: We don't add SELECT here for aggregate queries
        // The calling method (CountAsync, SumAsync, etc.) will handle the aggregate

        // WHERE - Transform logical column names to physical
        ApplyPhysicalWhereConditions(query, _whereConditions, table, useBaseTableAlias ? "base_table" : null);

        // Apply RLS expression if available
        if (!string.IsNullOrEmpty(_rlsExpression))
        {
            query.WhereRaw(_rlsExpression);
        }

        // Add lookup JOINs from expansion using structured join info
        if (_lookupExpansion?.Joins.Count > 0)
        {
            foreach (var join in _lookupExpansion.Joins)
            {
                // SqlKata LeftJoin with aliased table
                // Format: LEFT JOIN "target" AS alias ON base_table."source_col" = alias."target_col"
                query.LeftJoin(
                    $"{join.TargetTablePhysical} AS {join.TargetTableAlias}",
                    $"base_table.{join.SourceColumnPhysical}",
                    $"{join.TargetTableAlias}.{join.TargetColumnPhysical}");
            }
        }

        // JOIN - resolve physical table and column names
        foreach (var (joinTableName, sourceColumn, targetColumn, isLeft) in _joins)
        {
            var joinTable = await GetJoinedTableMetadataAsync(joinTableName, cancellationToken);
            var physicalJoinTable = joinTable.PhysicalName;
            var physicalSourceColumn = GetPhysicalColumnName(sourceColumn, table);
            var physicalTargetColumn = GetPhysicalColumnName(targetColumn, joinTable);

            // Add table prefix when using base_table alias
            var sourceRef = useBaseTableAlias ? $"base_table.{physicalSourceColumn}" : physicalSourceColumn;

            if (isLeft)
            {
                query.LeftJoin(physicalJoinTable, sourceRef, physicalTargetColumn);
            }
            else
            {
                query.Join(physicalJoinTable, sourceRef, physicalTargetColumn);
            }
        }

        // ORDER BY
        foreach (var (column, descending) in _orderByClauses)
        {
            var physicalColumn = GetPhysicalColumnName(column, table);
            var columnRef = useBaseTableAlias ? $"base_table.{physicalColumn}" : physicalColumn;
            if (descending)
            {
                query.OrderByDesc(columnRef);
            }
            else
            {
                query.OrderBy(columnRef);
            }
        }

        // GROUP BY
        if (_groupByColumns.Count > 0)
        {
            var physicalGroupBy = _groupByColumns
                .Select(c =>
                {
                    var physCol = GetPhysicalColumnName(c, table);
                    return useBaseTableAlias ? $"base_table.{physCol}" : physCol;
                })
                .ToArray();
            query.GroupBy(physicalGroupBy);
        }

        // HAVING
        foreach (var condition in _havingConditions)
        {
            var physicalColumn = GetPhysicalColumnName(condition.Column, table);
            var columnRef = useBaseTableAlias ? $"base_table.{physicalColumn}" : physicalColumn;
            var (sqlOp, value) = GetSqlOperator(condition.Operator, condition.Value);
            query.HavingRaw($"{columnRef} {sqlOp} ?", value);
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

        // Build lookup expansion for lookup columns
        await BuildLookupExpansionAsync(table, cancellationToken);

        // Build rollup expansion for rollup columns
        await BuildRollupExpansionAsync(table, cancellationToken);

        var query = await BuildPhysicalQueryAsync(cancellationToken);

        // Determine if we're using base_table alias (when lookups or rollups are present)
        var hasLookups = _lookupExpansion?.HasExpansion == true;
        var hasRollups = _rollupExpansion?.HasExpansion == true;
        var useBaseTableAlias = hasLookups || hasRollups;

        // Add SELECT clause
        if (_selectAllCalled || (_selectedColumns.Count == 0 && _aggregates.Count == 0))
        {
            if (useBaseTableAlias)
            {
                // Select all base table columns with alias
                query.Select("base_table.*");

                // Add lookup column expressions
                if (hasLookups)
                {
                    foreach (var (logicalName, selectExpr) in _lookupExpansion!.SelectExpressions)
                    {
                        query.SelectRaw($"{selectExpr} AS \"{logicalName}\"");
                    }
                }

                // Add rollup column expressions (correlated subqueries)
                if (hasRollups)
                {
                    foreach (var (logicalName, subqueryExpr) in _rollupExpansion!.SubqueryExpressions)
                    {
                        query.SelectRaw($"{subqueryExpr} AS \"{logicalName}\"");
                    }
                }
            }
            else
            {
                // Standard SELECT * without alias
                query.Select("*");
            }
        }
        else if (_selectedColumns.Count > 0)
        {
            foreach (var column in _selectedColumns)
            {
                // Check if this is a lookup column with expansion
                if (_lookupExpansion?.SelectExpressions.TryGetValue(column, out var lookupExpr) == true)
                {
                    query.SelectRaw($"{lookupExpr} AS \"{column}\"");
                }
                // Check if this is a rollup column with expansion
                else if (_rollupExpansion?.SubqueryExpressions.TryGetValue(column, out var rollupExpr) == true)
                {
                    query.SelectRaw($"{rollupExpr} AS \"{column}\"");
                }
                else
                {
                    var physicalColumn = GetPhysicalColumnName(column, table);
                    if (useBaseTableAlias)
                    {
                        query.Select($"base_table.{physicalColumn}");
                    }
                    else
                    {
                        query.Select(physicalColumn);
                    }
                }
            }
        }

        // Aggregates
        foreach (var (function, column, alias) in _aggregates)
        {
            var physicalColumn = GetPhysicalColumnName(column, table);
            var columnRef = useBaseTableAlias ? $"base_table.{physicalColumn}" : physicalColumn;
            var aggExpr = BuildAggregateExpression(function, columnRef);
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

    /// <summary>
    /// Builds lookup expansion for columns that are lookup type.
    /// </summary>
    private async Task BuildLookupExpansionAsync(TableMetadata table, CancellationToken cancellationToken)
    {
        if (_lookupResolver is null)
            return;

        // Find lookup columns that need expansion
        var lookupColumns = new List<LookupColumnInfo>();

        foreach (var column in table.Columns)
        {
            if (column.LookupConfig is null)
                continue;

            // If SelectAll or this column is in the selected columns
            if (_selectAllCalled || _selectedColumns.Count == 0 || _selectedColumns.Contains(column.LogicalName))
            {
                lookupColumns.Add(new LookupColumnInfo
                {
                    ColumnName = column.LogicalName,
                    Config = column.LookupConfig,
                    DataType = column.DataType
                });
            }
        }

        if (lookupColumns.Count == 0)
            return;

        _lookupExpansion = await _lookupResolver.BuildLookupExpansionAsync(
            _tenantId,
            table,
            lookupColumns,
            cancellationToken);
    }

    /// <summary>
    /// Builds rollup expansion for columns that are rollup type.
    /// Rollups generate correlated subqueries for aggregate values.
    /// </summary>
    private async Task BuildRollupExpansionAsync(TableMetadata table, CancellationToken cancellationToken)
    {
        if (_rollupResolver is null)
            return;

        // Find rollup columns that need expansion
        var rollupColumns = new List<RollupColumnInfo>();

        foreach (var column in table.Columns)
        {
            if (column.RollupConfig is null)
                continue;

            // If SelectAll or this column is in the selected columns
            if (_selectAllCalled || _selectedColumns.Count == 0 || _selectedColumns.Contains(column.LogicalName))
            {
                rollupColumns.Add(new RollupColumnInfo
                {
                    ColumnName = column.LogicalName,
                    Config = column.RollupConfig,
                    DataType = column.DataType
                });
            }
        }

        if (rollupColumns.Count == 0)
            return;

        _rollupExpansion = await _rollupResolver.BuildRollupExpansionAsync(
            _tenantId,
            table,
            rollupColumns,
            cancellationToken);
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
        TableMetadata table,
        string? tableAlias = null)
    {
        foreach (var condition in conditions)
        {
            var physicalColumn = GetPhysicalColumnName(condition.Column, table);
            var columnRef = string.IsNullOrEmpty(tableAlias)
                ? physicalColumn
                : $"{tableAlias}.{physicalColumn}";
            ApplyWhereCondition(query, columnRef, condition);
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

        // Load RLS expression for SELECT operations
        await LoadRlsExpressionAsync(PolicyType.Select, cancellationToken);

        return _tableMetadata;
    }

    private async Task<TableMetadata> GetJoinedTableMetadataAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (_joinedTableMetadata.TryGetValue(tableName, out var cached))
            return cached;

        var metadata = await _metadataRepository.GetTableByNameAsync(
            _tenantId, tableName, includeColumns: true, cancellationToken);

        if (metadata is null)
            throw new InvalidOperationException($"Joined table '{tableName}' not found for tenant '{_tenantId}'");

        _joinedTableMetadata[tableName] = metadata;
        return metadata;
    }

    private async Task LoadRlsExpressionAsync(PolicyType policyType, CancellationToken cancellationToken)
    {
        if (_rlsExpression is not null)
            return;

        var securityContext = _securityContextAccessor.ContextOrNull;
        if (securityContext is null)
        {
            // No security context, allow all (anonymous access)
            return;
        }

        _rlsExpression = await _securityPolicyService.EvaluatePoliciesAsync(
            _tenantId,
            _tableName,
            policyType,
            securityContext,
            cancellationToken);
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
