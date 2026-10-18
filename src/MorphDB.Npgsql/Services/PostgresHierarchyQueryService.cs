using System.Text;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of hierarchy query service using recursive CTEs.
/// Supports ancestors, descendants, path, siblings, and cycle detection queries.
/// </summary>
public sealed class PostgresHierarchyQueryService : IHierarchyQueryService
{
    private readonly MorphConnectionFactory _connections;
    private readonly IMetadataRepository _metadataRepository;
    private const int DefaultMaxDepth = 100;

    public PostgresHierarchyQueryService(
        MorphConnectionFactory connections,
        IMetadataRepository metadataRepository)
    {
        _connections = connections;
        _metadataRepository = metadataRepository;
    }

    public async Task<HierarchyQueryResult> GetAncestorsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.ProjectId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Build recursive CTE for ancestors
        var sql = BuildAncestorsCte(table.PhysicalName, StoredColumn(table, request.ParentColumn, "parentColumn"), StoredColumns(table, request.Columns), maxDepth, request.IncludeSelf);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, table.Columns, request.IncludeSelf, request.RecordId);

        return new HierarchyQueryResult
        {
            Records = records,
            TotalCount = records.Count,
            MaxDepth = records.Count > 0 ? records.Max(r => r.Depth) : 0,
            ReachedMaxDepth = records.Any(r => r.Depth >= maxDepth)
        };
    }

    public async Task<HierarchyQueryResult> GetDescendantsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.ProjectId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Build recursive CTE for descendants
        var sql = BuildDescendantsCte(table.PhysicalName, StoredColumn(table, request.ParentColumn, "parentColumn"), StoredColumns(table, request.Columns), maxDepth, OrderTerms(table, request.OrderBy), request.IncludeSelf);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, table.Columns, request.IncludeSelf, request.RecordId);

        return new HierarchyQueryResult
        {
            Records = records,
            TotalCount = records.Count,
            MaxDepth = records.Count > 0 ? records.Max(r => r.Depth) : 0,
            ReachedMaxDepth = records.Any(r => r.Depth >= maxDepth)
        };
    }

    public async Task<HierarchyQueryResult> GetPathToRootAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.ProjectId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Path to root is ancestors in reverse order (from root to record)
        var sql = BuildPathToRootCte(table.PhysicalName, StoredColumn(table, request.ParentColumn, "parentColumn"), StoredColumns(table, request.Columns), maxDepth);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, table.Columns, includeSelf: true, request.RecordId);

        return new HierarchyQueryResult
        {
            Records = records,
            TotalCount = records.Count,
            MaxDepth = records.Count > 0 ? records.Max(r => r.Depth) : 0,
            ReachedMaxDepth = records.Count >= maxDepth
        };
    }

    public async Task<HierarchyQueryResult> GetSiblingsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.ProjectId, request.TableName, cancellationToken);

        // Get siblings (same parent, exclude self unless requested)
        var sql = BuildSiblingsSql(table.PhysicalName, StoredColumn(table, request.ParentColumn, "parentColumn"), StoredColumns(table, request.Columns), request.IncludeSelf);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = results.Select(row => new HierarchyRecord
        {
            Data = RowMapper.MapToLogicalDictionary(row, table.Columns),
            Depth = 0,
            Path = null
        }).ToList();

        return new HierarchyQueryResult
        {
            Records = records,
            TotalCount = records.Count,
            MaxDepth = 0,
            ReachedMaxDepth = false
        };
    }

    public async Task<HierarchyQueryResult> GetSubtreeAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Subtree is self + descendants
        var descendantsRequest = request with { IncludeSelf = true };
        return await GetDescendantsAsync(descendantsRequest, cancellationToken);
    }

    public async Task<bool> WouldCreateCycleAsync(
        CycleCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.ProjectId, request.TableName, cancellationToken);

        // Check if the new parent is a descendant of the record
        // If so, setting it as parent would create a cycle
        var sql = BuildCycleCheckCte(table.PhysicalName, StoredColumn(table, request.ParentColumn, "parentColumn"));

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var wouldCycle = await connection.ExecuteScalarAsync<bool>(sql, new
        {
            recordId = request.RecordId,
            newParentId = request.NewParentId
        });

        return wouldCycle;
    }

    public async Task<CycleDetectionResult> DetectCyclesAsync(
        Guid projectId,
        string tableName,
        string parentColumn,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(projectId, tableName, cancellationToken);

        // Use PostgreSQL 14+ CYCLE detection feature
        var sql = BuildCycleDetectionCte(table.PhysicalName, StoredColumn(table, parentColumn, "parentColumn"));

        await using var connection = await _connections.OpenAsync(cancellationToken);
        var cyclicIds = await connection.QueryAsync<Guid>(sql);
        var cyclicIdList = cyclicIds.ToList();

        return new CycleDetectionResult
        {
            HasCycles = cyclicIdList.Count > 0,
            CyclicRecordIds = cyclicIdList,
            CycleDescriptions = cyclicIdList.Count > 0
                ? [$"Found {cyclicIdList.Count} records involved in cycles"]
                : []
        };
    }

    #region SQL Builders

    private static string BuildAncestorsCte(
        string physicalName,
        string parentColumn,
        List<string>? columns,
        int maxDepth,
        bool includeSelf)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = SelectList(columns, "depth");
        var lowest = includeSelf ? 0 : 1;

        // The walk starts at the record itself (depth 0), so includeSelf can keep it; the parent is
        // depth 1 either way.
        return $"""
            WITH RECURSIVE ancestors AS (
                SELECT t.*, 0 AS depth
                FROM {tableName} t
                WHERE t."_id" = @recordId

                UNION ALL

                SELECT t.*, a.depth + 1
                FROM {tableName} t
                INNER JOIN ancestors a ON t."_id" = a.{parentCol}
                WHERE a.depth < {maxDepth}
            )
            SELECT {selectCols} FROM ancestors WHERE depth >= {lowest} ORDER BY depth ASC
            """;
    }

    private static string BuildDescendantsCte(
        string physicalName,
        string parentColumn,
        List<string>? columns,
        int maxDepth,
        string? orderBy,
        bool includeSelf)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = SelectList(columns, "depth");
        var lowest = includeSelf ? 0 : 1;

        var orderClause = string.IsNullOrEmpty(orderBy)
            ? "depth ASC"
            : $"depth ASC, {orderBy}";

        // The walk starts at the record itself (depth 0) — what a subtree's root is — and its
        // children are depth 1 either way.
        return $"""
            WITH RECURSIVE descendants AS (
                SELECT t.*, 0 AS depth
                FROM {tableName} t
                WHERE t."_id" = @recordId

                UNION ALL

                SELECT t.*, d.depth + 1
                FROM {tableName} t
                INNER JOIN descendants d ON t.{parentCol} = d."_id"
                WHERE d.depth < {maxDepth}
            )
            SELECT {selectCols} FROM descendants WHERE depth >= {lowest} ORDER BY {orderClause}
            """;
    }

    private static string BuildPathToRootCte(
        string physicalName,
        string parentColumn,
        List<string>? columns,
        int maxDepth)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = SelectList(columns, "depth", "path");

        return $"""
            WITH RECURSIVE path_to_root AS (
                -- Base case: start with the given record
                SELECT t.*, 0 AS depth, ARRAY[t."_id"] AS path
                FROM {tableName} t
                WHERE t."_id" = @recordId

                UNION ALL

                -- Recursive case: get parent
                SELECT t.*, p.depth + 1, p.path || t."_id"
                FROM {tableName} t
                INNER JOIN path_to_root p ON t."_id" = p.{parentCol}
                WHERE p.depth < {maxDepth} AND NOT t."_id" = ANY(p.path)
            )
            SELECT {selectCols} FROM path_to_root ORDER BY depth DESC
            """;
    }

    private static string BuildSiblingsSql(
        string physicalName,
        string parentColumn,
        List<string>? columns,
        bool includeSelf)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = columns == null || columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(DdlBuilder.QuoteIdentifier));

        var selfFilter = includeSelf ? "" : $"AND s.\"_id\" != @recordId";

        return $"""
            SELECT {selectCols}
            FROM {tableName} s
            WHERE EXISTS (SELECT 1 FROM {tableName} WHERE "_id" = @recordId)
              AND s.{parentCol} IS NOT DISTINCT FROM (SELECT {parentCol} FROM {tableName} WHERE "_id" = @recordId)
            {selfFilter}
            ORDER BY s."_id"
            """;
    }

    private static string BuildCycleCheckCte(string physicalName, string parentColumn)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);

        // Check if newParentId is a descendant of recordId
        // If it is, then setting newParentId as parent of recordId would create a cycle
        return $"""
            WITH RECURSIVE descendants AS (
                SELECT "_id"
                FROM {tableName}
                WHERE {parentCol} = @recordId

                UNION ALL

                SELECT t."_id"
                FROM {tableName} t
                INNER JOIN descendants d ON t.{parentCol} = d."_id"
            )
            SELECT EXISTS (SELECT 1 FROM descendants WHERE "_id" = @newParentId)
            """;
    }

    private static string BuildCycleDetectionCte(string physicalName, string parentColumn)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);

        // Use PostgreSQL 14+ CYCLE clause for detection
        return $"""
            WITH RECURSIVE hierarchy AS (
                SELECT "_id", {parentCol}, ARRAY["_id"] AS path, false AS is_cycle
                FROM {tableName}
                WHERE {parentCol} IS NOT NULL

                UNION ALL

                SELECT t."_id", t.{parentCol}, h.path || t."_id",
                       t."_id" = ANY(h.path)
                FROM {tableName} t
                INNER JOIN hierarchy h ON t.{parentCol} = h."_id"
                WHERE NOT t."_id" = ANY(h.path)
            )
            SELECT DISTINCT "_id" FROM hierarchy WHERE is_cycle
            """;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// The select list of a query over a recursive CTE that carries the table's columns and the
    /// columns the walk computes (<paramref name="computed"/> — <c>depth</c>, <c>path</c>). With no
    /// columns asked for, <c>*</c> is all of them: naming a computed column beside it would return
    /// it twice, and a row cannot hold two values under one name.
    /// </summary>
    private static string SelectList(List<string>? columns, params string[] computed) =>
        columns == null || columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(DdlBuilder.QuoteIdentifier).Concat(computed));

    /// <summary>
    /// The physical name of the stored or system column a caller named by its declared name. A name
    /// the table does not store is refused here, so nothing a caller wrote reaches SQL as written.
    /// </summary>
    private static string StoredColumn(Core.Models.TableMetadata table, string name, string parameter)
    {
        var column = table.Columns.FirstOrDefault(c => c.LogicalName == name);
        if (column is { IsDerived: false })
        {
            return column.PhysicalName;
        }

        if (column is null && Core.Models.SystemColumns.IsSystemColumn(name))
        {
            return name;
        }

        throw new Core.Exceptions.ValidationException(parameter, $"'{name}' is not a stored column of '{table.LogicalName}'.");
    }

    private static List<string>? StoredColumns(Core.Models.TableMetadata table, IReadOnlyList<string>? names) =>
        names is null || names.Count == 0 ? null : names.Select(name => StoredColumn(table, name, "columns")).ToList();

    /// <summary>
    /// The caller's order as <c>ORDER BY</c> terms the server wrote: parsed as
    /// <c>column [asc|desc]</c> over the table's stored columns, never pasted.
    /// </summary>
    private static string? OrderTerms(Core.Models.TableMetadata table, string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return null;
        }

        var error = DeclaredOrder.Errors(orderBy, table).FirstOrDefault();
        if (error is not null)
        {
            throw new Core.Exceptions.ValidationException("orderBy", error);
        }

        return DeclaredOrder.Terms(orderBy, table, alias: null);
    }

    private async Task<Core.Models.TableMetadata> GetTableMetadataAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken)
    {
        // With its columns: a row is handed back in the names the table was declared with.
        var table = await _metadataRepository.GetTableByNameAsync(projectId, tableName, includeColumns: true, cancellationToken);
        if (table == null)
        {
            throw new KeyNotFoundException($"Table '{tableName}' not found");
        }
        return table;
    }

    private static List<HierarchyRecord> MapToHierarchyRecords(
        IEnumerable<dynamic> results,
        IReadOnlyList<Core.Models.ColumnMetadata> columns,
        bool includeSelf,
        Guid selfId)
    {
        var records = new List<HierarchyRecord>();

        foreach (var row in results)
        {
            // Logical column names, and no internal column — as every other read hands a row back.
            // The columns the walk computed are not the table's, and pass through under their own
            // names.
            Dictionary<string, object?> data = RowMapper.MapToLogicalDictionary(row, columns);

            // Extract depth from the row
            var depth = data.TryGetValue("depth", out var d) && d is int depthValue ? depthValue : 0;

            // Extract path if present
            IReadOnlyList<Guid>? path = null;
            if (data.TryGetValue("path", out var p) && p is Guid[] pathArray)
            {
                path = pathArray;
            }

            // Skip self if not requested
            if (!includeSelf && data.TryGetValue("_id", out var idValue) && idValue is Guid id && id == selfId)
            {
                continue;
            }

            // The computed columns are the record's own properties, not part of its row
            data.Remove("depth");
            data.Remove("path");

            records.Add(new HierarchyRecord
            {
                Data = data,
                Depth = depth,
                Path = path
            });
        }

        return records;
    }

    #endregion
}
