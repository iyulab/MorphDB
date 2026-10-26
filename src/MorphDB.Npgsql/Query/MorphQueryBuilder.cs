using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using SqlKata.Compilers;
using SqlKataQuery = SqlKata.Query;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// PostgreSQL implementation of IMorphQueryBuilder using SqlKata.
/// Provides fluent query building with logical-to-physical name translation. Row-level security
/// is not this builder's to apply: PostgreSQL enforces the table's policies on the statement it
/// runs, for the caller its connection was opened for.
/// </summary>
public sealed class MorphQueryBuilder : IMorphQueryBuilder
{
    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly IFormulaResolver? _formulaResolver;
    private readonly Guid _projectId;

    /// <summary>
    /// Creates a new MorphQueryBuilder.
    /// </summary>
    public MorphQueryBuilder(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository,
        Guid projectId,
        ILookupResolver? lookupResolver = null,
        IRollupResolver? rollupResolver = null,
        IFormulaResolver? formulaResolver = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _metadataRepository = metadataRepository ?? throw new ArgumentNullException(nameof(metadataRepository));
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _formulaResolver = formulaResolver;
        _projectId = projectId;
    }

    /// <inheritdoc />
    public IMorphQuery From(string tableName)
    {
        return new MorphQuery(
            _connections,
            _metadataRepository,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver,
            _projectId,
            tableName,
            null);
    }

    /// <summary>
    /// A query over a table whose definition the caller has already read, so the query does not read it
    /// again — the definition is what the query would load by <paramref name="tableName"/>.
    /// </summary>
    internal IMorphQuery From(string tableName, TableMetadata table) =>
        new MorphQuery(
            _connections,
            _metadataRepository,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver,
            _projectId,
            tableName,
            null,
            table);

    /// <inheritdoc />
    public IMorphQuery From(string tableName, string tableAlias)
    {
        return new MorphQuery(
            _connections,
            _metadataRepository,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver,
            _projectId,
            tableName,
            tableAlias);
    }
}

/// <summary>
/// PostgreSQL implementation of IMorphQuery using SqlKata.
/// Stores query operations with logical names, then builds SQL with physical names at execution.
/// Supports automatic lookup column expansion via JOINs.
/// Supports automatic rollup column expansion via correlated subqueries.
/// Supports automatic formula column expansion via SQL expressions.
/// </summary>
internal sealed class MorphQuery : IMorphQuery
{
    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILookupResolver? _lookupResolver;
    private readonly IRollupResolver? _rollupResolver;
    private readonly IFormulaResolver? _formulaResolver;
    private readonly Guid _projectId;
    private readonly string _tableName;
    private readonly string? _tableAlias;
    private readonly PostgresCompiler _compiler;

    private TableMetadata? _tableMetadata;
    private readonly Dictionary<string, TableMetadata> _joinedTableMetadata = new();
    private TableSource? _source;

    // Store all query operations with logical names
    private bool _selectAllCalled;
    private readonly List<string> _selectedColumns = [];
    private readonly List<(AggregateFunction Function, string Column, string? Alias)> _aggregates = [];
    private readonly List<WhereCondition> _whereConditions = [];

    // Where conditions go: the query's own list, or a group's while its builder runs.
    private List<WhereCondition> _conditionTarget;
    private readonly List<(string Table, string SourceColumn, string TargetColumn, bool IsLeft)> _joins = [];
    private readonly List<(string Column, bool Descending)> _orderByClauses = [];
    private readonly List<string> _groupByColumns = [];
    private readonly List<WhereCondition> _havingConditions = [];
    private int? _limit;
    private int? _offset;
    private bool _includeDeleted;

    private sealed record WhereCondition(
        string Column,
        FilterOperator Operator,
        object? Value,
        bool IsOr,
        ConditionType Type,
        IReadOnlyList<WhereCondition>? Children = null);

    private enum ConditionType
    {
        Standard,
        In,
        NotIn,
        Null,
        NotNull,
        Group
    }

    internal MorphQuery(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository,
        ILookupResolver? lookupResolver,
        IRollupResolver? rollupResolver,
        IFormulaResolver? formulaResolver,
        Guid projectId,
        string tableName,
        string? tableAlias,
        TableMetadata? table = null)
    {
        _connections = connections;
        _metadataRepository = metadataRepository;
        _lookupResolver = lookupResolver;
        _rollupResolver = rollupResolver;
        _formulaResolver = formulaResolver;
        _projectId = projectId;
        _tableName = tableName;
        _tableAlias = tableAlias;
        _tableMetadata = table;
        _compiler = new PostgresCompiler();
        _conditionTarget = _whereConditions;
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
    public IMorphQuery IncludeDeleted()
    {
        _includeDeleted = true;
        _source = null;
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
        _conditionTarget.Add(new WhereCondition(column, op, value, false, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery AndWhere(string column, FilterOperator op, object? value)
    {
        _conditionTarget.Add(new WhereCondition(column, op, value, false, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery OrWhere(string column, FilterOperator op, object? value)
    {
        _conditionTarget.Add(new WhereCondition(column, op, value, true, ConditionType.Standard));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereGroup(Func<IMorphQuery, IMorphQuery> group) => AddGroup(group, isOr: false);

    public IMorphQuery OrWhereGroup(Func<IMorphQuery, IMorphQuery> group) => AddGroup(group, isOr: true);

    private MorphQuery AddGroup(Func<IMorphQuery, IMorphQuery> group, bool isOr)
    {
        ArgumentNullException.ThrowIfNull(group);
        var children = new List<WhereCondition>();
        var outer = _conditionTarget;
        _conditionTarget = children;
        try
        {
            group(this);
        }
        finally
        {
            _conditionTarget = outer;
        }

        if (children.Count > 0)
        {
            _conditionTarget.Add(new WhereCondition(string.Empty, FilterOperator.Equals, null, isOr, ConditionType.Group, children));
        }

        return this;
    }

    public IMorphQuery WhereIn(string column, IEnumerable<object> values)
    {
        _conditionTarget.Add(new WhereCondition(column, FilterOperator.Equals, values.ToArray(), false, ConditionType.In));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNotIn(string column, IEnumerable<object> values)
    {
        _conditionTarget.Add(new WhereCondition(column, FilterOperator.Equals, values.ToArray(), false, ConditionType.NotIn));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNull(string column)
    {
        _conditionTarget.Add(new WhereCondition(column, FilterOperator.Equals, null, false, ConditionType.Null));
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery WhereNotNull(string column)
    {
        _conditionTarget.Add(new WhereCondition(column, FilterOperator.Equals, null, false, ConditionType.NotNull));
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
        _limit = Math.Min(count, QueryLimits.MaxPageSize);
        return this;
    }

    /// <inheritdoc />
    public IMorphQuery Offset(int count)
    {
        _offset = count;
        return this;
    }

    #endregion

    #region EXECUTION

    /// <inheritdoc />
    public async Task<IReadOnlyList<IDictionary<string, object?>>> ToListAsync(
        CancellationToken cancellationToken = default)
    {
        var (sql, parameters) = await CompileQueryAsync(cancellationToken);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        var results = await connection.QueryAsync<dynamic>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var table = await GetTableMetadataAsync(cancellationToken);
        var mappedResults = new List<IDictionary<string, object?>>();
        foreach (var row in results)
        {
            mappedResults.Add(Infrastructure.RowMapper.MapToLogicalDictionary(row, table.Columns));
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

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        var result = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal?> SumAsync(string column, CancellationToken cancellationToken = default)
    {
        var physicalColumn = (await GetSourceAsync(cancellationToken)).Column(column);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var sumQuery = query.AsSum(physicalColumn);
        var compiled = _compiler.Compile(sumQuery);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        var result = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<decimal?> AvgAsync(string column, CancellationToken cancellationToken = default)
    {
        var physicalColumn = (await GetSourceAsync(cancellationToken)).Column(column);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var avgQuery = query.AsAverage(physicalColumn);
        var compiled = _compiler.Compile(avgQuery);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        var result = await connection.ExecuteScalarAsync<decimal?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<T?> MinAsync<T>(string column, CancellationToken cancellationToken = default)
    {
        var physicalColumn = (await GetSourceAsync(cancellationToken)).Column(column);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var minQuery = query.AsMin(physicalColumn);
        var compiled = _compiler.Compile(minQuery);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
        var result = await connection.ExecuteScalarAsync<T?>(
            new CommandDefinition(compiled.Sql, compiled.NamedBindings, cancellationToken: cancellationToken));

        return result;
    }

    /// <inheritdoc />
    public async Task<T?> MaxAsync<T>(string column, CancellationToken cancellationToken = default)
    {
        var physicalColumn = (await GetSourceAsync(cancellationToken)).Column(column);

        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var maxQuery = query.AsMax(physicalColumn);
        var compiled = _compiler.Compile(maxQuery);

        await using var lease = await _connections.LeaseAsync(cancellationToken);

        var connection = lease.Connection;
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
        var query = new SqlKataQuery(string.IsNullOrEmpty(_tableAlias) ? _tableName : $"{_tableName} as {_tableAlias}");

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
        foreach (var (column, descending) in EffectiveOrder())
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
            ApplyHaving(query, condition.Column, condition.Operator, condition.Value);
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
    /// The table as this read selects it: itself, or with its lookup, rollup and formula columns
    /// computed by name (<see cref="TableSource"/>). Every clause below names columns through it.
    /// </summary>
    private async Task<TableSource> GetSourceAsync(CancellationToken cancellationToken) =>
        _source ??= await TableSource.CreateAsync(
            _projectId,
            await GetTableMetadataAsync(cancellationToken),
            _tableAlias,
            _lookupResolver,
            _rollupResolver,
            _formulaResolver,
            cancellationToken,
            activeRowsOnly: !_includeDeleted);

    /// <summary>
    /// The order a query is read in. A page of rows (a limit or an offset, no grouping) ends its order
    /// with <c>_id</c>, so the order is total: rows that tie on the requested keys — or every row, when
    /// none are requested — fall in the same place on every read, and consecutive pages neither repeat
    /// nor skip a row. Without it PostgreSQL may answer ties in any order, page by page.
    /// </summary>
    private List<(string Column, bool Descending)> EffectiveOrder()
    {
        var paged = (_limit.HasValue || _offset.HasValue) && _groupByColumns.Count == 0 && _aggregates.Count == 0;
        if (!paged || _orderByClauses.Exists(c => string.Equals(c.Column, SystemColumns.Id, StringComparison.Ordinal)))
        {
            return _orderByClauses;
        }

        return [.. _orderByClauses, (SystemColumns.Id, false)];
    }

    /// <summary>
    /// Builds a SqlKata query with physical names for actual execution — FROM, WHERE, joins,
    /// ORDER BY, GROUP BY, HAVING and paging. The SELECT list is the caller's
    /// (<see cref="CompileQueryAsync"/>, or an aggregate such as <see cref="CountAsync"/>).
    /// </summary>
    private async Task<SqlKataQuery> BuildPhysicalQueryAsync(CancellationToken cancellationToken)
    {
        var source = await GetSourceAsync(cancellationToken);
        var query = new SqlKataQuery();
        source.ApplyFrom(query);

        // WHERE - a derived column filters like a stored one; an exists reads its related table, named
        // here once so the statement builds without waiting
        var related = await RelatedRowsSql.ResolveAsync(
            Flatten(_whereConditions).Where(c => c.Operator.TakesRelatedRows()).Select(c => c.Value),
            GetJoinedTableMetadataAsync,
            cancellationToken);
        var table = await GetTableMetadataAsync(cancellationToken);
        foreach (var condition in _whereConditions)
        {
            ApplyWhereCondition(query, source.Column, Typed(condition, table), related);
        }

        // JOIN - resolve physical table and column names
        foreach (var (joinTableName, sourceColumn, targetColumn, isLeft) in _joins)
        {
            var joinTable = await GetJoinedTableMetadataAsync(joinTableName, cancellationToken);
            var physicalJoinTable = joinTable.PhysicalName;
            var targetRef = $"{physicalJoinTable}.{GetPhysicalColumnName(targetColumn, joinTable)}";

            if (isLeft)
            {
                query.LeftJoin(physicalJoinTable, source.Column(sourceColumn), targetRef);
            }
            else
            {
                query.Join(physicalJoinTable, source.Column(sourceColumn), targetRef);
            }
        }

        // ORDER BY
        foreach (var (column, descending) in EffectiveOrder())
        {
            if (descending)
            {
                query.OrderByDesc(source.Column(column));
            }
            else
            {
                query.OrderBy(source.Column(column));
            }
        }

        // GROUP BY
        if (_groupByColumns.Count > 0)
        {
            query.GroupBy(_groupByColumns.Select(source.ColumnOrSelf).ToArray());
        }

        // HAVING
        foreach (var condition in _havingConditions)
        {
            ApplyHaving(query, _compiler.Wrap(source.ColumnOrSelf(condition.Column)), condition.Operator, condition.Value);
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
        var query = await BuildPhysicalQueryAsync(cancellationToken);
        var source = await GetSourceAsync(cancellationToken);

        // Add SELECT clause
        if (_selectAllCalled || (_selectedColumns.Count == 0 && _aggregates.Count == 0))
        {
            query.Select($"{source.Name}.*");
        }
        else
        {
            foreach (var column in _selectedColumns)
            {
                query.Select(source.Column(column));
            }
        }

        // Aggregates
        foreach (var (function, column, alias) in _aggregates)
        {
            var aggExpr = BuildAggregateExpression(function, _compiler.Wrap(source.ColumnOrSelf(column)));
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
            ApplyWhereCondition(query, static column => column, condition);
        }
    }

    /// <summary>
    /// The filter of an UPDATE or DELETE, which addresses the table itself: a derived column is
    /// computed by reads only, so a write cannot be narrowed by one.
    /// </summary>
    private static void ApplyPhysicalWhereConditions(
        SqlKataQuery query,
        List<WhereCondition> conditions,
        TableMetadata table)
    {
        string Physical(string column) =>
            table.Columns.FirstOrDefault(c => c.LogicalName == column) is { IsDerived: true }
                ? throw new ValidationException(
                    column,
                    "a derived column (lookup, rollup or formula) is computed by reads and cannot narrow a write.")
                : GetPhysicalColumnName(column, table);

        foreach (var condition in conditions)
        {
            ApplyWhereCondition(query, Physical, Typed(condition, table));
        }
    }

    /// <summary>
    /// The condition with its value read as its column's type (<see cref="ColumnValues"/>) — each value of a list, and
    /// each condition of a group. An exists keeps its own conditions, which are read against its related table.
    /// </summary>
    private static WhereCondition Typed(WhereCondition condition, TableMetadata table) => condition.Type switch
    {
        ConditionType.Group => condition with { Children = [.. condition.Children!.Select(c => Typed(c, table))] },
        // A list condition keeps its operator in its type; the column's value reading wants to be told which it is.
        ConditionType.In => condition with { Value = ColumnValues.OfCondition(table, condition.Column, FilterOperator.In, condition.Value) },
        ConditionType.NotIn => condition with { Value = ColumnValues.OfCondition(table, condition.Column, FilterOperator.NotIn, condition.Value) },
        ConditionType.Standard =>
            condition with { Value = ColumnValues.OfCondition(table, condition.Column, condition.Operator, condition.Value) },
        _ => condition,
    };

    /// <summary>Every condition, a group's included, as one list.</summary>
    private static IEnumerable<WhereCondition> Flatten(IEnumerable<WhereCondition> conditions) =>
        conditions.SelectMany(c => c.Type == ConditionType.Group ? Flatten(c.Children!) : [c]);

    /// <summary>
    /// One condition, its column named through <paramref name="columnOf"/> — a group's conditions inside
    /// parentheses, joined to what precedes it by its own AND or OR. An exists reads its table from
    /// <paramref name="related"/>; without one (a write's filter) it is refused.
    /// </summary>
    private static void ApplyWhereCondition(
        SqlKataQuery query, Func<string, string> columnOf, WhereCondition condition, IReadOnlyDictionary<string, TableMetadata>? related = null)
    {
        if (condition.Type == ConditionType.Group)
        {
            SqlKataQuery Nested(SqlKataQuery inner)
            {
                foreach (var child in condition.Children!)
                {
                    ApplyWhereCondition(inner, columnOf, child, related);
                }

                return inner;
            }

            if (condition.IsOr)
            {
                query.OrWhere(Nested);
            }
            else
            {
                query.Where(Nested);
            }

            return;
        }

        var column = columnOf(condition.Column);
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

            case ConditionType.Standard when condition.Operator.TakesRelatedRows():
                var exists = RelatedRowsSql.Build(condition.Column, condition.Value, column, related,
                    static (inner, innerColumn, op, value) => ApplyStandardWhereCondition(inner, innerColumn, op, value, isOr: false));
                switch (condition.Operator, condition.IsOr)
                {
                    case (FilterOperator.Exists, false):
                        query.WhereExists(exists);
                        break;
                    case (FilterOperator.Exists, true):
                        query.OrWhereExists(exists);
                        break;
                    case (_, false):
                        query.WhereNotExists(exists);
                        break;
                    default:
                        query.OrWhereNotExists(exists);
                        break;
                }
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
                var containsPattern = LikePattern.Contains(value);
                if (isOr)
                    query.OrWhereLike(column, containsPattern, caseSensitive: false);
                else
                    query.WhereLike(column, containsPattern, caseSensitive: false);
                break;
            case FilterOperator.StartsWith:
                var startsWithPattern = LikePattern.StartsWith(value);
                if (isOr)
                    query.OrWhereLike(column, startsWithPattern, caseSensitive: false);
                else
                    query.WhereLike(column, startsWithPattern, caseSensitive: false);
                break;
            case FilterOperator.EndsWith:
                var endsWithPattern = LikePattern.EndsWith(value);
                if (isOr)
                    query.OrWhereLike(column, endsWithPattern, caseSensitive: false);
                else
                    query.WhereLike(column, endsWithPattern, caseSensitive: false);
                break;
            case FilterOperator.IsNull:
                if (isOr)
                    query.OrWhereNull(column);
                else
                    query.WhereNull(column);
                break;
            case FilterOperator.IsNotNull:
                if (isOr)
                    query.OrWhereNotNull(column);
                else
                    query.WhereNotNull(column);
                break;
            case FilterOperator.In:
                if (isOr)
                    query.OrWhereIn(column, ListOf(op, value));
                else
                    query.WhereIn(column, ListOf(op, value));
                break;
            case FilterOperator.NotIn:
                if (isOr)
                    query.OrWhereNotIn(column, ListOf(op, value));
                else
                    query.WhereNotIn(column, ListOf(op, value));
                break;
            default:
                // A condition this switch does not apply used to be dropped without a word, so the query
                // came back wider than asked. Between carries a range and is refused here.
                throw new NotSupportedException($"Where does not take the operator {op}.");
        }
    }

    /// <summary>
    /// The values an <c>in</c> or <c>not in</c> condition lists. A single value is refused rather than read as
    /// a list of one: a caller who meant a list and sent text would otherwise get an answer to another question.
    /// </summary>
    private static object[] ListOf(FilterOperator op, object? value) => value switch
    {
        string or null => throw new ArgumentException(
            $"The operator {op} takes a list of values, and was given {(value is null ? "none" : "one")}."),
        System.Collections.IEnumerable list => list.Cast<object>().ToArray(),
        _ => throw new ArgumentException($"The operator {op} takes a list of values, and was given one."),
    };

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
            AggregateFunction.ArrayAgg => $"ARRAY_AGG({column} ORDER BY {column})",
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unsupported aggregate function.")
        };
    }

    private static void ApplyHaving(SqlKataQuery query, string column, FilterOperator op, object? value)
    {
        switch (op)
        {
            case FilterOperator.IsNull:
                query.HavingRaw($"{column} IS NULL");
                break;
            case FilterOperator.IsNotNull:
                query.HavingRaw($"{column} IS NOT NULL");
                break;
            default:
                var (sqlOp, sqlValue) = GetSqlOperator(op, value);
                query.HavingRaw($"{column} {sqlOp} ?", sqlValue);
                break;
        }
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
            FilterOperator.Contains => ("ILIKE", LikePattern.Contains(value)),
            FilterOperator.StartsWith => ("ILIKE", LikePattern.StartsWith(value)),
            FilterOperator.EndsWith => ("ILIKE", LikePattern.EndsWith(value)),
            // Falling back to "=" turned an operator this map does not know into a different condition.
            _ => throw new NotSupportedException($"HAVING does not take the operator {op}.")
        };
    }

    /// <summary>
    /// Resolves a logical column to its physical name, failing loudly on a name the table does not
    /// declare. Before this check an unknown filter column flowed straight into SQL and PostgreSQL's
    /// 42703 surfaced as a 500 — the caller's typo reported as our defect. System columns pass
    /// through: they are legitimately addressable but not part of the declared column set.
    /// </summary>
    private static string GetPhysicalColumnName(string logicalName, TableMetadata table)
    {
        var column = table.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        if (column is not null)
            return column.PhysicalName;

        if (SystemColumns.IsSystemColumn(logicalName))
            return logicalName;

        throw new ColumnNotFoundException(table.LogicalName, logicalName);
    }

    private async Task<TableMetadata> GetTableMetadataAsync(CancellationToken cancellationToken)
    {
        if (_tableMetadata is not null)
            return _tableMetadata;

        _tableMetadata = await _metadataRepository.GetTableByNameAsync(
            _projectId, _tableName, includeColumns: true, cancellationToken);

        // The domain type, not InvalidOperationException: this is how a caller's typo'd table (or a
        // project that does not exist — no project, no tables) becomes the documented 404 instead of
        // a 500. The message also stays free of the project GUID (hidden-layer principle).
        if (_tableMetadata is null)
            throw new TableNotFoundException(_tableName);

        return _tableMetadata;
    }

    private async Task<TableMetadata> GetJoinedTableMetadataAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (_joinedTableMetadata.TryGetValue(tableName, out var cached))
            return cached;

        var metadata = await _metadataRepository.GetTableByNameAsync(
            _projectId, tableName, includeColumns: true, cancellationToken);

        if (metadata is null)
            throw new TableNotFoundException(tableName);

        _joinedTableMetadata[tableName] = metadata;
        return metadata;
    }

    #endregion
}
