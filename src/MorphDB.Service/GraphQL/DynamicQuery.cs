using HotChocolate;
using HotChocolate.Types;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// Dynamic GraphQL query type for MorphDB tables.
/// </summary>
[ExtendObjectType(typeof(Query))]
public sealed class DynamicQuery
{
    /// <summary>
    /// Lists all tables for the current tenant.
    /// </summary>
    [GraphQLDescription("Lists all tables for the current tenant")]
    public async Task<IReadOnlyList<TableGraphType>> GetTables(
        [Service] ISchemaManager schemaManager,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.TenantId;
        var tables = await schemaManager.ListTablesAsync(tenantId, cancellationToken);

        return tables.Select(t => new TableGraphType
        {
            Name = t.LogicalName,
            PhysicalName = t.PhysicalName,
            Version = t.SchemaVersion,
            Columns = t.Columns.Select(c => new ColumnGraphType
            {
                Name = c.LogicalName,
                Type = c.DataType.ToString(),
                Nullable = c.IsNullable,
                Unique = c.IsUnique,
                Indexed = c.IsIndexed
            }).ToList(),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        }).ToList();
    }

    /// <summary>
    /// Gets a specific table by name.
    /// </summary>
    [GraphQLDescription("Gets a specific table by name")]
    public async Task<TableGraphType?> GetTable(
        string name,
        [Service] ISchemaManager schemaManager,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.TenantId;
        var table = await schemaManager.GetTableAsync(tenantId, name, cancellationToken);

        if (table is null)
            return null;

        return new TableGraphType
        {
            Name = table.LogicalName,
            PhysicalName = table.PhysicalName,
            Version = table.SchemaVersion,
            Columns = table.Columns.Select(c => new ColumnGraphType
            {
                Name = c.LogicalName,
                Type = c.DataType.ToString(),
                Nullable = c.IsNullable,
                Unique = c.IsUnique,
                Indexed = c.IsIndexed
            }).ToList(),
            Indexes = table.Indexes.Select(i => new IndexGraphType
            {
                Name = i.LogicalName,
                Type = i.IndexType.ToString(),
                Unique = i.IsUnique,
                Columns = i.Columns.Select(ic =>
                    table.Columns.FirstOrDefault(c => c.ColumnId == ic.ColumnId)?.LogicalName ?? "unknown").ToList()
            }).ToList(),
            Relations = table.Relations.Select(r => new RelationGraphType
            {
                Name = r.LogicalName,
                Type = r.RelationType.ToString(),
                SourceColumn = table.Columns.FirstOrDefault(c => c.ColumnId == r.SourceColumnId)?.LogicalName ?? "unknown",
                TargetTableId = r.TargetTableId,
                TargetColumnId = r.TargetColumnId
            }).ToList(),
            CreatedAt = table.CreatedAt,
            UpdatedAt = table.UpdatedAt
        };
    }

    /// <summary>
    /// Queries records from a table with pagination and filtering.
    /// </summary>
    [GraphQLDescription("Queries records from a table with pagination and filtering")]
    public async Task<RecordConnection> GetRecords(
        string table,
        int? first,
        string? after,
        string? filter,
        string? orderBy,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.TenantId;
        var query = dataService.Query(tenantId).From(table).SelectAll();

        // Apply filters
        if (!string.IsNullOrEmpty(filter))
        {
            query = ApplyFilter(query, filter);
        }

        // Apply ordering
        if (!string.IsNullOrEmpty(orderBy))
        {
            query = ApplyOrdering(query, orderBy);
        }
        else
        {
            query = query.OrderByDesc("created_at");
        }

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var pageSize = first ?? 20;
        if (pageSize > 100) pageSize = 100;

        if (!string.IsNullOrEmpty(after))
        {
            var cursorValue = DecodeCursor(after);
            query = query.After("id", cursorValue, pageSize);
        }
        else
        {
            query = query.Limit(pageSize);
        }

        var records = await query.ToListAsync(cancellationToken);

        var edges = records.Select(r => new RecordEdge
        {
            Node = CreateRecordNode(r),
            Cursor = EncodeCursor(GetRecordId(r))
        }).ToList();

        return new RecordConnection
        {
            Edges = edges,
            PageInfo = new PageInfo
            {
                HasNextPage = records.Count == pageSize,
                HasPreviousPage = !string.IsNullOrEmpty(after),
                StartCursor = edges.FirstOrDefault()?.Cursor,
                EndCursor = edges.LastOrDefault()?.Cursor,
                TotalCount = (int)totalCount
            },
            TotalCount = (int)totalCount
        };
    }

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    [GraphQLDescription("Gets a single record by ID")]
    public async Task<RecordNode?> GetRecord(
        string table,
        Guid id,
        [Service] IMorphDataService dataService,
        [Service] ITenantContextAccessor tenantAccessor,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.TenantId;
        var record = await dataService.GetByIdAsync(tenantId, table, id, cancellationToken);

        if (record is null)
            return null;

        var node = CreateRecordNode(record);
        // Override the ID with the one from the parameter since we know it
        return new RecordNode
        {
            Id = id,
            Data = node.Data,
            CreatedAt = node.CreatedAt,
            UpdatedAt = node.UpdatedAt
        };
    }

    private static IMorphQuery ApplyFilter(IMorphQuery query, string filter)
    {
        var parts = filter.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var segments = part.Split(':', 3);
            if (segments.Length != 3) continue;

            var column = segments[0];
            var op = ParseOperator(segments[1]);
            var value = ParseValue(segments[2]);

            query = query.AndWhere(column, op, value);
        }

        return query;
    }

    private static IMorphQuery ApplyOrdering(IMorphQuery query, string orderBy)
    {
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var segments = part.Split(':', 2);
            var column = segments[0];
            var direction = segments.Length > 1 ? segments[1].ToLowerInvariant() : "asc";

            query = direction == "desc" ? query.OrderByDesc(column) : query.OrderBy(column);
        }

        return query;
    }

    private static FilterOperator ParseOperator(string op) => op.ToLowerInvariant() switch
    {
        "eq" => FilterOperator.Equals,
        "ne" or "neq" => FilterOperator.NotEquals,
        "gt" => FilterOperator.GreaterThan,
        "gte" or "ge" => FilterOperator.GreaterThanOrEquals,
        "lt" => FilterOperator.LessThan,
        "lte" or "le" => FilterOperator.LessThanOrEquals,
        "like" => FilterOperator.Like,
        "ilike" => FilterOperator.ILike,
        "contains" => FilterOperator.Contains,
        "startswith" => FilterOperator.StartsWith,
        "endswith" => FilterOperator.EndsWith,
        _ => FilterOperator.Equals
    };

    private static object? ParseValue(string value)
    {
        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
            return null;
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        if (int.TryParse(value, out var intValue))
            return intValue;
        if (decimal.TryParse(value, out var decValue))
            return decValue;
        if (Guid.TryParse(value, out var guidValue))
            return guidValue;
        if (DateTimeOffset.TryParse(value, out var dateValue))
            return dateValue;
        return value;
    }

    private static string EncodeCursor(Guid id) =>
        Convert.ToBase64String(id.ToByteArray());

    private static Guid DecodeCursor(string cursor)
    {
        try
        {
            return new Guid(Convert.FromBase64String(cursor));
        }
        catch
        {
            return Guid.Empty;
        }
    }

    private static RecordNode CreateRecordNode(IDictionary<string, object?> r)
    {
        return new RecordNode
        {
            Id = GetRecordId(r),
            Data = r,
            CreatedAt = r.TryGetValue("created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }

    private static Guid GetRecordId(IDictionary<string, object?> r)
    {
        return r.TryGetValue("id", out var idValue) && idValue is Guid id ? id : Guid.Empty;
    }
}

#region GraphQL Types

/// <summary>
/// GraphQL representation of a table.
/// </summary>
public sealed class TableGraphType
{
    public required string Name { get; init; }
    public required string PhysicalName { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<ColumnGraphType> Columns { get; init; } = [];
    public IReadOnlyList<IndexGraphType> Indexes { get; init; } = [];
    public IReadOnlyList<RelationGraphType> Relations { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// GraphQL representation of a column.
/// </summary>
public sealed class ColumnGraphType
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Nullable { get; init; }
    public bool Unique { get; init; }
    public bool Indexed { get; init; }
}

/// <summary>
/// GraphQL representation of an index.
/// </summary>
public sealed class IndexGraphType
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Unique { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = [];
}

/// <summary>
/// GraphQL representation of a relation.
/// </summary>
public sealed class RelationGraphType
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string SourceColumn { get; init; }
    public Guid TargetTableId { get; init; }
    public Guid TargetColumnId { get; init; }
}

/// <summary>
/// A connection to a list of records (for pagination).
/// </summary>
public sealed class RecordConnection
{
    public IReadOnlyList<RecordEdge> Edges { get; init; } = [];
    public required PageInfo PageInfo { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>
/// An edge in a record connection.
/// </summary>
public sealed class RecordEdge
{
    public required RecordNode Node { get; init; }
    public required string Cursor { get; init; }
}

/// <summary>
/// A record node.
/// </summary>
public sealed class RecordNode
{
    public Guid Id { get; init; }

    [GraphQLType(typeof(AnyType))]
    public IDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Page information for pagination.
/// </summary>
public sealed class PageInfo
{
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
    public string? StartCursor { get; init; }
    public string? EndCursor { get; init; }
    public int TotalCount { get; init; }
}

#endregion
