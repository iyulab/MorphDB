using System.Text;
using Dapper;
using MorphDB.Core.Abstractions;
using MorphDB.Npgsql.Ddl;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of hierarchy query service using recursive CTEs.
/// Supports ancestors, descendants, path, siblings, and cycle detection queries.
/// </summary>
public sealed class PostgresHierarchyQueryService : IHierarchyQueryService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataRepository _metadataRepository;
    private const int DefaultMaxDepth = 100;

    public PostgresHierarchyQueryService(
        NpgsqlDataSource dataSource,
        IMetadataRepository metadataRepository)
    {
        _dataSource = dataSource;
        _metadataRepository = metadataRepository;
    }

    public async Task<HierarchyQueryResult> GetAncestorsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.TenantId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Build recursive CTE for ancestors
        var sql = BuildAncestorsCte(table.PhysicalName, request.ParentColumn, request.Columns, maxDepth);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, request.IncludeSelf, request.RecordId);

        return new HierarchyQueryResult
        {
            Records = records,
            TotalCount = records.Count,
            MaxDepth = records.Count > 0 ? records.Max(r => r.Depth) : 0,
            ReachedMaxDepth = records.Count >= maxDepth
        };
    }

    public async Task<HierarchyQueryResult> GetDescendantsAsync(
        HierarchyQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(request.TenantId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Build recursive CTE for descendants
        var sql = BuildDescendantsCte(table.PhysicalName, request.ParentColumn, request.Columns, maxDepth, request.OrderBy);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, request.IncludeSelf, request.RecordId);

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
        var table = await GetTableMetadataAsync(request.TenantId, request.TableName, cancellationToken);
        var maxDepth = request.MaxDepth ?? DefaultMaxDepth;

        // Path to root is ancestors in reverse order (from root to record)
        var sql = BuildPathToRootCte(table.PhysicalName, request.ParentColumn, request.Columns, maxDepth);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = MapToHierarchyRecords(results, includeSelf: true, request.RecordId);

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
        var table = await GetTableMetadataAsync(request.TenantId, request.TableName, cancellationToken);

        // Get siblings (same parent, exclude self unless requested)
        var sql = BuildSiblingsSql(table.PhysicalName, request.ParentColumn, request.Columns, request.IncludeSelf);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var results = await connection.QueryAsync<dynamic>(sql, new { recordId = request.RecordId });

        var records = results.Select(row => new HierarchyRecord
        {
            Data = (IDictionary<string, object?>)row,
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
        var table = await GetTableMetadataAsync(request.TenantId, request.TableName, cancellationToken);

        // Check if the new parent is a descendant of the record
        // If so, setting it as parent would create a cycle
        var sql = BuildCycleCheckCte(table.PhysicalName, request.ParentColumn);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var wouldCycle = await connection.ExecuteScalarAsync<bool>(sql, new
        {
            recordId = request.RecordId,
            newParentId = request.NewParentId
        });

        return wouldCycle;
    }

    public async Task<CycleDetectionResult> DetectCyclesAsync(
        Guid tenantId,
        string tableName,
        string parentColumn,
        CancellationToken cancellationToken = default)
    {
        var table = await GetTableMetadataAsync(tenantId, tableName, cancellationToken);

        // Use PostgreSQL 14+ CYCLE detection feature
        var sql = BuildCycleDetectionCte(table.PhysicalName, parentColumn);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
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
        IReadOnlyList<string>? columns,
        int maxDepth)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = columns == null || columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(DdlBuilder.QuoteIdentifier));

        return $"""
            WITH RECURSIVE ancestors AS (
                -- Base case: start with the parent of the given record
                SELECT t.*, 1 AS depth
                FROM {tableName} t
                WHERE t."_id" = (SELECT {parentCol} FROM {tableName} WHERE "_id" = @recordId)

                UNION ALL

                -- Recursive case: get parent of current ancestor
                SELECT t.*, a.depth + 1
                FROM {tableName} t
                INNER JOIN ancestors a ON t."_id" = a.{parentCol}
                WHERE a.depth < {maxDepth}
            )
            SELECT {selectCols}, depth FROM ancestors ORDER BY depth ASC
            """;
    }

    private static string BuildDescendantsCte(
        string physicalName,
        string parentColumn,
        IReadOnlyList<string>? columns,
        int maxDepth,
        string? orderBy)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = columns == null || columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(DdlBuilder.QuoteIdentifier));

        var orderClause = string.IsNullOrEmpty(orderBy)
            ? "depth ASC"
            : $"depth ASC, {orderBy}";

        return $"""
            WITH RECURSIVE descendants AS (
                -- Base case: direct children
                SELECT t.*, 1 AS depth
                FROM {tableName} t
                WHERE t.{parentCol} = @recordId

                UNION ALL

                -- Recursive case: children of current descendants
                SELECT t.*, d.depth + 1
                FROM {tableName} t
                INNER JOIN descendants d ON t.{parentCol} = d."_id"
                WHERE d.depth < {maxDepth}
            )
            SELECT {selectCols}, depth FROM descendants ORDER BY {orderClause}
            """;
    }

    private static string BuildPathToRootCte(
        string physicalName,
        string parentColumn,
        IReadOnlyList<string>? columns,
        int maxDepth)
    {
        var tableName = DdlBuilder.QuoteIdentifier(physicalName);
        var parentCol = DdlBuilder.QuoteIdentifier(parentColumn);
        var selectCols = columns == null || columns.Count == 0
            ? "*"
            : string.Join(", ", columns.Select(DdlBuilder.QuoteIdentifier));

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
            SELECT {selectCols}, depth, path FROM path_to_root ORDER BY depth DESC
            """;
    }

    private static string BuildSiblingsSql(
        string physicalName,
        string parentColumn,
        IReadOnlyList<string>? columns,
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
            WHERE s.{parentCol} = (SELECT {parentCol} FROM {tableName} WHERE "_id" = @recordId)
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

    private async Task<Core.Models.TableMetadata> GetTableMetadataAsync(
        Guid tenantId,
        string tableName,
        CancellationToken cancellationToken)
    {
        var table = await _metadataRepository.GetTableByNameAsync(tenantId, tableName, cancellationToken: cancellationToken);
        if (table == null)
        {
            throw new KeyNotFoundException($"Table '{tableName}' not found");
        }
        return table;
    }

    private static List<HierarchyRecord> MapToHierarchyRecords(
        IEnumerable<dynamic> results,
        bool includeSelf,
        Guid selfId)
    {
        var records = new List<HierarchyRecord>();

        foreach (var row in results)
        {
            var data = (IDictionary<string, object?>)row;

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

            // Remove internal columns from data
            var cleanData = new Dictionary<string, object?>(data);
            cleanData.Remove("depth");
            cleanData.Remove("path");

            records.Add(new HierarchyRecord
            {
                Data = cleanData,
                Depth = depth,
                Path = path
            });
        }

        return records;
    }

    #endregion
}
