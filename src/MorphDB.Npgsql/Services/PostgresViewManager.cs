using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;
using MorphDB.Npgsql.Security;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of view management with advisory lock protection.
/// Supports both regular views and materialized views.
/// </summary>
public sealed class PostgresViewManager : IViewManager
{
    private readonly MorphConnectionFactory _connections;
    private readonly IViewMetadataRepository _viewRepository;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IAdvisoryLockManager _lockManager;
    private readonly INameHasher _nameHasher;
    private readonly IChangeLogger _changeLogger;
    private readonly SchemaManagerOptions _options;

    public PostgresViewManager(
        MorphConnectionFactory connections,
        IViewMetadataRepository viewRepository,
        IMetadataRepository metadataRepository,
        IAdvisoryLockManager lockManager,
        INameHasher nameHasher,
        IChangeLogger changeLogger,
        SchemaManagerOptions? options = null)
    {
        _connections = connections;
        _viewRepository = viewRepository;
        _metadataRepository = metadataRepository;
        _lockManager = lockManager;
        _nameHasher = nameHasher;
        _changeLogger = changeLogger;
        _options = options ?? new SchemaManagerOptions();
    }

    public async Task<ViewMetadata> CreateViewAsync(
        CreateViewRequest request,
        CancellationToken cancellationToken = default)
    {
        LogicalNameValidator.ValidateEntityName(request.Name, "View");

        // Check if view already exists
        var existing = await _viewRepository.GetViewByNameAsync(
            request.ProjectId, request.Name, cancellationToken);
        if (existing != null)
        {
            throw new DuplicateNameException("View", request.Name);
        }

        var viewId = Guid.NewGuid();
        var physicalName = _nameHasher.GenerateViewName(request.ProjectId, request.Name);

        // Acquire advisory lock for DDL
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"view:{viewId}",
            _options.LockTimeout,
            cancellationToken);

        // Build the SELECT statement from view definition
        var queryBuilder = new ViewQueryBuilder(_metadataRepository, request.ProjectId);
        var selectStatement = await queryBuilder.BuildSelectStatementAsync(request.Definition, cancellationToken);

        // Create the view in PostgreSQL
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        if (request.IsMaterialized)
        {
            await BuildMaterializedViewAsync(
                connection, request.Name, physicalName, selectStatement, request.Definition, replace: false, cancellationToken);
        }
        else
        {
            await connection.ExecuteAsync(DdlBuilder.BuildCreateView(physicalName, selectStatement));
        }

        // Build view columns metadata from definition
        var columns = BuildViewColumns(viewId, request.Definition);

        // Save view metadata
        var viewMetadata = new ViewMetadata
        {
            ViewId = viewId,
            ProjectId = request.ProjectId,
            LogicalName = request.Name,
            PhysicalName = physicalName,
            Definition = request.Definition,
            IsMaterialized = request.IsMaterialized,
            RefreshPolicy = request.RefreshPolicy,
            RefreshSchedule = request.RefreshSchedule,
            IsStale = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            Columns = columns
        };

        var savedView = await _viewRepository.InsertViewAsync(viewMetadata, cancellationToken);

        // Save column metadata
        foreach (var column in columns)
        {
            await _viewRepository.InsertViewColumnAsync(column, cancellationToken);
        }

        // Log the change
        await _changeLogger.LogViewCreatedAsync(viewId, request.Name, cancellationToken);

        return savedView with { Columns = columns };
    }

    public async Task<ViewMetadata?> GetViewAsync(
        Guid projectId,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        return await _viewRepository.GetViewByNameAsync(projectId, logicalName, cancellationToken);
    }

    public async Task<ViewMetadata?> GetViewByIdAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        return await _viewRepository.GetViewByIdAsync(viewId, cancellationToken);
    }

    public async Task<IReadOnlyList<ViewMetadata>> ListViewsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await _viewRepository.ListViewsAsync(projectId, cancellationToken);
    }

    public async Task<ViewMetadata> UpdateViewAsync(
        UpdateViewRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = await _viewRepository.GetViewByIdAsync(request.ViewId, cancellationToken);
        if (view == null)
        {
            throw new NotFoundException("View", request.ViewId.ToString());
        }

        // Acquire advisory lock for DDL
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"view:{request.ViewId}",
            _options.LockTimeout,
            cancellationToken);

        // If definition changed, recreate the view
        if (request.Definition != null)
        {
            var queryBuilder = new ViewQueryBuilder(_metadataRepository, view.ProjectId);
            var selectStatement = await queryBuilder.BuildSelectStatementAsync(request.Definition, cancellationToken);

            await using var connection = await _connections.OpenSystemAsync(cancellationToken);

            if (view.IsMaterialized)
            {
                // Drop and recreate materialized view (no CREATE OR REPLACE for mat views)
                await BuildMaterializedViewAsync(
                    connection, view.LogicalName, view.PhysicalName, selectStatement, request.Definition, replace: true, cancellationToken);
            }
            else
            {
                await connection.ExecuteAsync(DdlBuilder.BuildCreateOrReplaceView(view.PhysicalName, selectStatement));
            }
        }

        // Update metadata
        await _viewRepository.UpdateViewAsync(
            request.ViewId,
            request.Name,
            request.Definition,
            request.RefreshPolicy,
            request.RefreshSchedule,
            request.Description,
            cancellationToken);

        // Log the change
        await _changeLogger.LogViewUpdatedAsync(request.ViewId, request.Name, cancellationToken);

        return (await _viewRepository.GetViewByIdAsync(request.ViewId, cancellationToken))!;
    }

    public async Task DeleteViewAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        var view = await _viewRepository.GetViewByIdAsync(viewId, cancellationToken);
        if (view == null)
        {
            throw new NotFoundException("View", viewId.ToString());
        }

        // Acquire advisory lock for DDL
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"view:{viewId}",
            _options.LockTimeout,
            cancellationToken);

        // Drop the view from PostgreSQL
        await using var connection = await _connections.OpenSystemAsync(cancellationToken);
        if (view.IsMaterialized)
        {
            await connection.ExecuteAsync(DdlBuilder.BuildDropMaterializedView(view.PhysicalName));
        }
        else
        {
            await connection.ExecuteAsync(DdlBuilder.BuildDropView(view.PhysicalName));
        }

        // Soft delete metadata
        await _viewRepository.SoftDeleteViewAsync(viewId, cancellationToken);

        // Log the change
        await _changeLogger.LogViewDeletedAsync(viewId, view.LogicalName, cancellationToken);
    }

    public async Task RefreshMaterializedViewAsync(
        Guid viewId,
        bool concurrent = false,
        CancellationToken cancellationToken = default)
    {
        var view = await _viewRepository.GetViewByIdAsync(viewId, cancellationToken);
        if (view == null)
        {
            throw new NotFoundException("View", viewId.ToString());
        }

        if (!view.IsMaterialized)
        {
            throw new InvalidOperationException($"View '{view.LogicalName}' is not a materialized view.");
        }

        // Acquire advisory lock
        await using var lockHandle = await _lockManager.AcquireDdlLockAsync(
            $"view:refresh:{viewId}",
            _options.LockTimeout,
            cancellationToken);

        await using var connection = await _connections.OpenSystemAsync(cancellationToken);

        // A refresh copies the tables' rows again, as MorphDB itself and so past every policy. A
        // view an earlier version built over a table that has a policy is not filled a second time.
        await MaterializedViewPolicyGuard.EnsureReadsNoTableWithPolicyAsync(
            connection, transaction: null, view.LogicalName, view.PhysicalName, "refreshed", cancellationToken);

        // Refresh the materialized view
        var refreshSql = DdlBuilder.BuildRefreshMaterializedView(view.PhysicalName, concurrent: concurrent);
        await connection.ExecuteAsync(refreshSql);

        // Update refresh timestamp and clear stale status
        var refreshTime = DateTimeOffset.UtcNow;
        await _viewRepository.UpdateLastRefreshAsync(viewId, refreshTime, cancellationToken);
        await _viewRepository.UpdateStaleStatusAsync(viewId, false, cancellationToken);
    }

    public async Task<bool> IsMaterializedViewStaleAsync(
        Guid viewId,
        CancellationToken cancellationToken = default)
    {
        var view = await _viewRepository.GetViewByIdAsync(viewId, cancellationToken);
        if (view == null)
        {
            throw new NotFoundException("View", viewId.ToString());
        }

        if (!view.IsMaterialized)
        {
            return false; // Regular views are always "fresh"
        }

        // Check if any referenced tables have been modified since last refresh
        if (!view.LastRefreshedAt.HasValue)
        {
            return true; // Never refreshed
        }

        var lastRefresh = view.LastRefreshedAt.Value;

        // Check base table
        var baseTable = await _metadataRepository.GetTableByNameAsync(
            view.ProjectId, view.Definition.BaseTable, cancellationToken: cancellationToken);
        if (baseTable == null)
        {
            return true; // Base table not found, consider stale
        }

        if (baseTable.UpdatedAt > lastRefresh)
        {
            return true;
        }

        // Check all joined tables
        foreach (var join in view.Definition.Joins)
        {
            var joinedTable = await _metadataRepository.GetTableByNameAsync(
                view.ProjectId, join.Table, cancellationToken: cancellationToken);

            if (joinedTable == null)
            {
                // Joined table not found - could be an error or the table was deleted
                return true;
            }

            if (joinedTable.UpdatedAt > lastRefresh)
            {
                // Mark as stale and update metadata
                await _viewRepository.UpdateStaleStatusAsync(viewId, true, cancellationToken);
                return true;
            }
        }

        return false;
    }

    public async Task<ViewQueryResult> QueryViewAsync(
        ViewQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var view = await _viewRepository.GetViewByNameAsync(request.ProjectId, request.ViewName, cancellationToken);
        if (view == null)
        {
            throw new NotFoundException("View", request.ViewName);
        }

        // Build query against the view
        var parameters = new UntypedParameters();
        var sql = BuildViewQuerySql(view, request, parameters);
        var countParameters = new UntypedParameters();
        var countSql = BuildViewCountSql(view, request, countParameters);

        await using var connection = await _connections.OpenAsync(cancellationToken);

        // A plain view is read through its tables' policies. A materialized view is a copy that no
        // policy filters, and neither creating one over a table with a policy nor adding a policy
        // under one is allowed — but a database an earlier version served may hold the pair, and
        // nothing kept in memory would know when another instance made or unmade it. So each read
        // of a materialized view asks the catalog, at the cost of one statement; a plain view asks
        // nothing.
        if (view.IsMaterialized)
        {
            await MaterializedViewPolicyGuard.EnsureReadsNoTableWithPolicyAsync(
                connection, transaction: null, view.LogicalName, view.PhysicalName, "read", cancellationToken);
        }

        // Execute data query
        IEnumerable<dynamic> data;
        try
        {
            data = await connection.QueryAsync<dynamic>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }
        catch (PostgresException ex) when (IsDataException(ex) && request.Filters is { Count: > 0 })
        {
            throw await RefuseUnreadableFilterAsync(connection, view, request.Filters, ex, cancellationToken);
        }

        var results = data.Select(row => (IDictionary<string, object?>)row).ToList();

        // Execute count query
        var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, countParameters, cancellationToken: cancellationToken));

        var hasMore = request.Skip.GetValueOrDefault(0) + request.Take.GetValueOrDefault(results.Count) < totalCount;

        return new ViewQueryResult
        {
            Data = results,
            TotalCount = totalCount,
            HasMore = hasMore,
            Metadata = view
        };
    }

    /// <summary>An error of the "data exception" class: a value the database could not read as the type it was compared with.</summary>
    private static bool IsDataException(PostgresException ex) => ex.SqlState is { } state && state.StartsWith("22", StringComparison.Ordinal);

    /// <summary>
    /// A view's columns take the type of what they select, so the database is the one that knows whether a value reads
    /// as a column's type: a value it refuses is the caller's mistake, and what is refused names the column — the one
    /// condition that fails on its own — like every other door's. Found by asking about each condition by itself, which
    /// costs a statement per condition and only on this path.
    /// </summary>
    private static async Task<Exception> RefuseUnreadableFilterAsync(
        NpgsqlConnection connection, ViewMetadata view, IReadOnlyList<ViewFilterSpec> filters, PostgresException cause, CancellationToken cancellationToken)
    {
        foreach (var filter in filters)
        {
            var parameters = new UntypedParameters();
            var probe = $"SELECT 1 FROM {DdlBuilder.QuoteIdentifier(view.PhysicalName)} WHERE {BuildFilterCondition(view, filter, parameters)} LIMIT 0";
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(probe, parameters, cancellationToken: cancellationToken));
            }
            catch (PostgresException ex) when (IsDataException(ex))
            {
                var shown = filter.Value is System.Collections.IEnumerable list and not string
                    ? string.Join(", ", list.Cast<object?>())
                    : Convert.ToString(filter.Value, System.Globalization.CultureInfo.InvariantCulture);
                return new ValidationException(filter.Field, $"'{shown}' is not a value of the column's type.");
            }
        }

        return cause;
    }

    /// <summary>
    /// Creates a materialized view — in place of the one it has, when <paramref name="replace"/> —
    /// and fills it, unless it reads a table that has a row-level security policy.
    /// </summary>
    /// <remarks>
    /// One transaction: the view is created empty, PostgreSQL is asked what it reads, and only then
    /// is it filled. A refused view copies no row and leaves nothing behind, and the view it would
    /// have replaced is still there. Creating it also locks the tables it reads against the DDL
    /// that gives a table a policy until this transaction ends, so of a view and a policy created
    /// at the same moment the later one sees the earlier and is refused.
    /// </remarks>
    private static async Task BuildMaterializedViewAsync(
        NpgsqlConnection connection,
        string logicalName,
        string physicalName,
        string selectStatement,
        ViewDefinition definition,
        bool replace,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        async Task ExecuteAsync(string statement) => await connection.ExecuteAsync(
            new CommandDefinition(statement, transaction: transaction, cancellationToken: cancellationToken));

        if (replace)
        {
            await ExecuteAsync(DdlBuilder.BuildDropMaterializedView(physicalName));
        }

        await ExecuteAsync(DdlBuilder.BuildCreateMaterializedView(physicalName, selectStatement, withData: false));

        await MaterializedViewPolicyGuard.EnsureReadsNoTableWithPolicyAsync(
            connection, transaction, logicalName, physicalName, "created", cancellationToken);

        await ExecuteAsync(DdlBuilder.BuildRefreshMaterializedView(physicalName));

        // A unique index is what lets the view be refreshed CONCURRENTLY.
        var indexColumns = GetUniqueIndexColumns(definition);
        if (indexColumns.Count > 0)
        {
            await ExecuteAsync(DdlBuilder.BuildMaterializedViewUniqueIndex($"ux_{physicalName}", physicalName, indexColumns));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string BuildViewQuerySql(ViewMetadata view, ViewQueryRequest request, UntypedParameters parameters)
    {
        var sb = new System.Text.StringBuilder();

        // SELECT
        sb.Append("SELECT ");
        if (request.Columns != null && request.Columns.Count > 0)
        {
            sb.Append(string.Join(", ", request.Columns.Select(c => DdlBuilder.QuoteIdentifier(ViewColumn(view, c, "select")))));
        }
        else
        {
            sb.Append('*');
        }

        // FROM
        sb.Append(" FROM ");
        sb.Append(DdlBuilder.QuoteIdentifier(view.PhysicalName));

        // WHERE (project isolation built into view)
        AppendWhere(sb, view, request, parameters);

        // ORDER BY
        if (request.OrderBy != null && request.OrderBy.Count > 0)
        {
            sb.Append(" ORDER BY ");
            var orderClauses = new List<string>();
            foreach (var order in request.OrderBy)
            {
                var direction = order.Descending ? " DESC" : " ASC";
                orderClauses.Add($"{DdlBuilder.QuoteIdentifier(ViewColumn(view, order.Column, "orderBy"))}{direction}");
            }
            sb.Append(string.Join(", ", orderClauses));
        }

        // LIMIT/OFFSET
        if (request.Take.HasValue)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" LIMIT {request.Take.Value}");
        }
        if (request.Skip.HasValue)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" OFFSET {request.Skip.Value}");
        }

        return sb.ToString();
    }

    private static string BuildViewCountSql(ViewMetadata view, ViewQueryRequest request, UntypedParameters parameters)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ");
        sb.Append(DdlBuilder.QuoteIdentifier(view.PhysicalName));
        AppendWhere(sb, view, request, parameters);
        return sb.ToString();
    }

    private static void AppendWhere(System.Text.StringBuilder sb, ViewMetadata view, ViewQueryRequest request, UntypedParameters parameters)
    {
        if (request.Filters == null || request.Filters.Count == 0)
        {
            return;
        }

        sb.Append(" WHERE ");
        sb.Append(string.Join(" AND ", request.Filters.Select(f => BuildFilterCondition(view, f, parameters))));
    }

    /// <summary>
    /// A query-time condition on a view. Each operator means what it means on the data endpoint — the
    /// <c>contains</c> family matches its value literally and ignores case, <c>in</c> takes a list — and an
    /// operator the view cannot answer is refused rather than read as equality.
    /// </summary>
    private static string BuildFilterCondition(ViewMetadata view, ViewFilterSpec filter, UntypedParameters parameters)
    {
        var field = DdlBuilder.QuoteIdentifier(ViewColumn(view, filter.Field, "filter"));

        return filter.Operator switch
        {
            FilterOperator.Equals => $"{field} = {parameters.Add(filter.Value)}",
            FilterOperator.NotEquals => $"{field} <> {parameters.Add(filter.Value)}",
            FilterOperator.GreaterThan => $"{field} > {parameters.Add(filter.Value)}",
            FilterOperator.GreaterThanOrEquals => $"{field} >= {parameters.Add(filter.Value)}",
            FilterOperator.LessThan => $"{field} < {parameters.Add(filter.Value)}",
            FilterOperator.LessThanOrEquals => $"{field} <= {parameters.Add(filter.Value)}",
            FilterOperator.Like => $"{field} LIKE {parameters.Add(filter.Value)}",
            FilterOperator.NotLike => $"{field} NOT LIKE {parameters.Add(filter.Value)}",
            FilterOperator.ILike => $"{field} ILIKE {parameters.Add(filter.Value)}",
            FilterOperator.Contains => $"{field} ILIKE {parameters.Add(LikePattern.Contains(filter.Value))}",
            FilterOperator.StartsWith => $"{field} ILIKE {parameters.Add(LikePattern.StartsWith(filter.Value))}",
            FilterOperator.EndsWith => $"{field} ILIKE {parameters.Add(LikePattern.EndsWith(filter.Value))}",
            FilterOperator.In => ListCondition(field, "IN", "FALSE", filter.Value, parameters),
            FilterOperator.NotIn => ListCondition(field, "NOT IN", "TRUE", filter.Value, parameters),
            FilterOperator.IsNull => $"{field} IS NULL",
            FilterOperator.IsNotNull => $"{field} IS NOT NULL",
            _ => throw new ArgumentException($"Filter operator '{filter.Operator}' is not available on a view query."),
        };
    }

    /// <summary>An empty list matches nothing (<c>in</c>) or everything (<c>notin</c>), as on the data endpoint.</summary>
    private static string ListCondition(string field, string op, string whenEmpty, object? value, UntypedParameters parameters)
    {
        if (value is not System.Collections.IEnumerable list || value is string)
        {
            throw new ArgumentException("The values of in and notin are a list.");
        }

        var names = list.Cast<object?>().Select(parameters.Add).ToList();
        return names.Count == 0 ? whenEmpty : $"{field} {op} ({string.Join(", ", names)})";
    }

    /// <summary>
    /// A column the request names, checked against the view's columns so a misspelling is the caller's
    /// error, not a database error. A view whose columns were not recorded is passed through.
    /// </summary>
    private static string ViewColumn(ViewMetadata view, string name, string where)
    {
        if (view.Columns.Count > 0 && !view.Columns.Any(c => string.Equals(c.LogicalName, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"View '{view.LogicalName}' has no column '{name}' ({where}).");
        }

        return name;
    }

    /// <summary>
    /// Filter values bound as parameters of unknown type, so PostgreSQL reads each the way it reads a
    /// quoted literal: as the type of the column it is compared with. A value reaches the server as its
    /// invariant text — <c>1.5</c>, an instant with its offset — whatever culture the service runs in.
    /// </summary>
    private sealed class UntypedParameters : SqlMapper.IDynamicParameters
    {
        private readonly List<(string Name, string? Text)> _values = [];

        public string Add(object? value)
        {
            var name = $"f{_values.Count}";
            _values.Add((name, Text(JsonValueConverter.ToClrValue(value))));
            return "@" + name;
        }

        public void AddParameters(System.Data.IDbCommand command, SqlMapper.Identity identity)
        {
            var parameters = ((NpgsqlCommand)command).Parameters;
            foreach (var (name, text) in _values)
            {
                parameters.Add(new NpgsqlParameter(name, NpgsqlTypes.NpgsqlDbType.Unknown) { Value = (object?)text ?? DBNull.Value });
            }
        }

        private static string? Text(object? value) => value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            DateTimeOffset instant => instant.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateTime instant => instant.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateOnly day => day.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static List<ViewColumnMetadata> BuildViewColumns(Guid viewId, ViewDefinition definition)
    {
        var columns = new List<ViewColumnMetadata>();
        var ordinal = 1;

        foreach (var col in definition.Columns)
        {
            columns.Add(new ViewColumnMetadata
            {
                ColumnId = Guid.NewGuid(),
                ViewId = viewId,
                LogicalName = col.Alias,
                DataType = col.DataType ?? MorphDataType.Text,
                IsComputed = !string.IsNullOrEmpty(col.Expression),
                Expression = col.Expression,
                OrdinalPosition = ordinal++
            });
        }

        return columns;
    }

    /// <summary>
    /// Gets columns suitable for a unique index on a materialized view.
    /// Prefers _id column if available, otherwise uses all non-computed columns.
    /// This enables CONCURRENTLY refresh which requires a unique index on all columns.
    /// </summary>
    private static List<string> GetUniqueIndexColumns(ViewDefinition definition)
    {
        // Look for _id column first (most reliable unique identifier)
        var idColumn = definition.Columns.FirstOrDefault(c =>
            c.Alias.Equals("_id", StringComparison.OrdinalIgnoreCase) ||
            c.Source?.EndsWith("._id", StringComparison.OrdinalIgnoreCase) == true);

        if (idColumn != null)
        {
            return [idColumn.Alias];
        }

        // If no _id, look for any column that seems like a primary key
        var pkColumn = definition.Columns.FirstOrDefault(c =>
            c.Alias.EndsWith("_id", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(c.Expression));

        if (pkColumn != null)
        {
            return [pkColumn.Alias];
        }

        // Fall back to all non-computed, non-aggregated columns
        var candidateColumns = definition.Columns
            .Where(c => string.IsNullOrEmpty(c.Expression) && c.Aggregation == null)
            .Select(c => c.Alias)
            .ToList();

        return candidateColumns;
    }
}
