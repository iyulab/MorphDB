using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Query;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of view management with advisory lock protection.
/// Supports both regular views and materialized views.
/// </summary>
public sealed class PostgresViewManager : IViewManager
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IViewMetadataRepository _viewRepository;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IAdvisoryLockManager _lockManager;
    private readonly INameHasher _nameHasher;
    private readonly IChangeLogger _changeLogger;
    private readonly SchemaManagerOptions _options;

    public PostgresViewManager(
        NpgsqlDataSource dataSource,
        IViewMetadataRepository viewRepository,
        IMetadataRepository metadataRepository,
        IAdvisoryLockManager lockManager,
        INameHasher nameHasher,
        IChangeLogger changeLogger,
        SchemaManagerOptions? options = null)
    {
        _dataSource = dataSource;
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
        string ddl;
        if (request.IsMaterialized)
        {
            ddl = DdlBuilder.BuildCreateMaterializedView(physicalName, selectStatement);
        }
        else
        {
            ddl = DdlBuilder.BuildCreateView(physicalName, selectStatement);
        }

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(ddl);

        // For materialized views, create a unique index to enable CONCURRENTLY refresh
        if (request.IsMaterialized)
        {
            var indexColumns = GetUniqueIndexColumns(request.Definition);
            if (indexColumns.Count > 0)
            {
                var indexName = $"ux_{physicalName}";
                var indexDdl = DdlBuilder.BuildMaterializedViewUniqueIndex(
                    indexName, physicalName, indexColumns);
                await connection.ExecuteAsync(indexDdl);
            }
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

            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            if (view.IsMaterialized)
            {
                // Drop and recreate materialized view (no CREATE OR REPLACE for mat views)
                await connection.ExecuteAsync(DdlBuilder.BuildDropMaterializedView(view.PhysicalName));
                await connection.ExecuteAsync(DdlBuilder.BuildCreateMaterializedView(view.PhysicalName, selectStatement));
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
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
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

        // Refresh the materialized view
        var refreshSql = DdlBuilder.BuildRefreshMaterializedView(view.PhysicalName, concurrent: concurrent);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
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
        var sql = BuildViewQuerySql(view, request);
        var countSql = BuildViewCountSql(view, request);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Execute data query
        var data = await connection.QueryAsync<dynamic>(sql);
        var results = data.Select(row => (IDictionary<string, object?>)row).ToList();

        // Execute count query
        var totalCount = await connection.ExecuteScalarAsync<long>(countSql);

        var hasMore = request.Skip.GetValueOrDefault(0) + request.Take.GetValueOrDefault(results.Count) < totalCount;

        return new ViewQueryResult
        {
            Data = results,
            TotalCount = totalCount,
            HasMore = hasMore,
            Metadata = view
        };
    }

    private static string BuildViewQuerySql(ViewMetadata view, ViewQueryRequest request)
    {
        var sb = new System.Text.StringBuilder();

        // SELECT
        sb.Append("SELECT ");
        if (request.Columns != null && request.Columns.Count > 0)
        {
            sb.Append(string.Join(", ", request.Columns.Select(DdlBuilder.QuoteIdentifier)));
        }
        else
        {
            sb.Append('*');
        }

        // FROM
        sb.Append(" FROM ");
        sb.Append(DdlBuilder.QuoteIdentifier(view.PhysicalName));

        // WHERE (project isolation built into view)
        if (request.Filters != null && request.Filters.Count > 0)
        {
            sb.Append(" WHERE ");
            var conditions = new List<string>();
            foreach (var filter in request.Filters)
            {
                conditions.Add(BuildFilterCondition(filter));
            }
            sb.Append(string.Join(" AND ", conditions));
        }

        // ORDER BY
        if (request.OrderBy != null && request.OrderBy.Count > 0)
        {
            sb.Append(" ORDER BY ");
            var orderClauses = new List<string>();
            foreach (var order in request.OrderBy)
            {
                var direction = order.Descending ? " DESC" : " ASC";
                orderClauses.Add($"{DdlBuilder.QuoteIdentifier(order.Column)}{direction}");
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

    private static string BuildViewCountSql(ViewMetadata view, ViewQueryRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ");
        sb.Append(DdlBuilder.QuoteIdentifier(view.PhysicalName));

        if (request.Filters != null && request.Filters.Count > 0)
        {
            sb.Append(" WHERE ");
            var conditions = new List<string>();
            foreach (var filter in request.Filters)
            {
                conditions.Add(BuildFilterCondition(filter));
            }
            sb.Append(string.Join(" AND ", conditions));
        }

        return sb.ToString();
    }

    private static string BuildFilterCondition(ViewFilterSpec filter)
    {
        var field = DdlBuilder.QuoteIdentifier(filter.Field);
        var value = FormatValue(filter.Value);

        return filter.Operator switch
        {
            FilterOperator.Equals => $"{field} = {value}",
            FilterOperator.NotEquals => $"{field} <> {value}",
            FilterOperator.GreaterThan => $"{field} > {value}",
            FilterOperator.GreaterThanOrEquals => $"{field} >= {value}",
            FilterOperator.LessThan => $"{field} < {value}",
            FilterOperator.LessThanOrEquals => $"{field} <= {value}",
            FilterOperator.Like => $"{field} LIKE {value}",
            FilterOperator.ILike => $"{field} ILIKE {value}",
            FilterOperator.In => $"{field} IN ({value})",
            FilterOperator.NotIn => $"{field} NOT IN ({value})",
            FilterOperator.IsNull => $"{field} IS NULL",
            FilterOperator.IsNotNull => $"{field} IS NOT NULL",
            FilterOperator.Between => $"{field} BETWEEN {value}",
            FilterOperator.Contains => $"{field} LIKE '%' || {value} || '%'",
            FilterOperator.StartsWith => $"{field} LIKE {value} || '%'",
            FilterOperator.EndsWith => $"{field} LIKE '%' || {value}",
            FilterOperator.NotLike => $"{field} NOT LIKE {value}",
            _ => $"{field} = {value}"
        };
    }

    private static string FormatValue(object? value)
    {
        // Filter values cross the API boundary (and JSONB storage) as JsonElement; unwrap so the
        // type checks below quote/render them correctly instead of falling through to ToString().
        value = JsonValueConverter.ToClrValue(value);

        if (value == null)
            return "NULL";
        if (value is string s)
            return $"'{s.Replace("'", "''")}'";
        if (value is bool b)
            return b ? "true" : "false";
        return value.ToString() ?? "NULL";
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
