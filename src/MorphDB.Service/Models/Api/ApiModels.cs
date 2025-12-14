using System.Text.Json.Serialization;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.Models.Api;

#region Common Response Models

/// <summary>
/// Standard API error response.
/// </summary>
public sealed record ErrorResponse
{
    public required string Error { get; init; }
    public string? Message { get; init; }
    public string? Code { get; init; }
    public IDictionary<string, string[]>? Details { get; init; }
}

/// <summary>
/// Paginated response wrapper.
/// </summary>
public sealed record PagedResponse<T>
{
    public required IReadOnlyList<T> Data { get; init; }
    public required PaginationInfo Pagination { get; init; }
}

public sealed record PaginationInfo
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

#endregion

#region Schema API Models

/// <summary>
/// Request to create a new table.
/// </summary>
public sealed record CreateTableApiRequest
{
    public required string Name { get; init; }
    public IReadOnlyList<CreateColumnApiRequest> Columns { get; init; } = [];
}

/// <summary>
/// Request to create a column.
/// </summary>
public sealed record CreateColumnApiRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Nullable { get; init; } = true;
    public bool Unique { get; init; }
    public bool Indexed { get; init; }
    public string? Default { get; init; }
}

/// <summary>
/// Request to update a table.
/// </summary>
public sealed record UpdateTableApiRequest
{
    public string? Name { get; init; }
    public int Version { get; init; }
}

/// <summary>
/// Request to add a column to existing table.
/// </summary>
public sealed record AddColumnApiRequest
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Nullable { get; init; } = true;
    public bool Unique { get; init; }
    public bool Indexed { get; init; }
    public string? Default { get; init; }
}

/// <summary>
/// Request to update a column.
/// </summary>
public sealed record UpdateColumnApiRequest
{
    public string? Name { get; init; }
    public string? Default { get; init; }
    public int Version { get; init; }
}

/// <summary>
/// Request to create an index.
/// </summary>
public sealed record CreateIndexApiRequest
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public string Type { get; init; } = "btree";
    public bool Unique { get; init; }
    public string? Where { get; init; }
}

/// <summary>
/// Request to create a relation.
/// </summary>
public sealed record CreateRelationApiRequest
{
    public required string Name { get; init; }
    public required string SourceTable { get; init; }
    public required string SourceColumn { get; init; }
    public required string TargetTable { get; init; }
    public required string TargetColumn { get; init; }
    public string Type { get; init; } = "one-to-many";
    public string OnDelete { get; init; } = "no-action";
}

/// <summary>
/// Table API response.
/// </summary>
public sealed record TableApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public int Version { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<ColumnApiResponse> Columns { get; init; } = [];
    public IReadOnlyList<IndexApiResponse> Indexes { get; init; } = [];
    public IReadOnlyList<RelationApiResponse> Relations { get; init; } = [];

    public static TableApiResponse FromMetadata(TableMetadata table) => new()
    {
        Id = table.TableId,
        Name = table.LogicalName,
        Version = table.SchemaVersion,
        CreatedAt = table.CreatedAt,
        UpdatedAt = table.UpdatedAt,
        Columns = table.Columns.Select(ColumnApiResponse.FromMetadata).ToList(),
        Indexes = table.Indexes.Select(IndexApiResponse.FromMetadata).ToList(),
        Relations = table.Relations.Select(RelationApiResponse.FromMetadata).ToList()
    };
}

/// <summary>
/// Column API response.
/// </summary>
public sealed record ColumnApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool Nullable { get; init; }
    public bool Unique { get; init; }
    public bool PrimaryKey { get; init; }
    public bool Indexed { get; init; }
    public string? Default { get; init; }
    public int Position { get; init; }

    public static ColumnApiResponse FromMetadata(ColumnMetadata column) => new()
    {
        Id = column.ColumnId,
        Name = column.LogicalName,
        Type = column.DataType.ToString().ToLowerInvariant(),
        Nullable = column.IsNullable,
        Unique = column.IsUnique,
        PrimaryKey = column.IsPrimaryKey,
        Indexed = column.IsIndexed,
        Default = column.DefaultValue,
        Position = column.OrdinalPosition
    };
}

/// <summary>
/// Index API response.
/// </summary>
public sealed record IndexApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required string Type { get; init; }
    public bool Unique { get; init; }

    public static IndexApiResponse FromMetadata(IndexMetadata index) => new()
    {
        Id = index.IndexId,
        Name = index.LogicalName,
        Columns = index.Columns.Select(c => c.PhysicalName).ToList(),
        Type = index.IndexType.ToString().ToLowerInvariant(),
        Unique = index.IsUnique
    };
}

/// <summary>
/// Relation API response.
/// </summary>
public sealed record RelationApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid SourceTableId { get; init; }
    public Guid SourceColumnId { get; init; }
    public Guid TargetTableId { get; init; }
    public Guid TargetColumnId { get; init; }
    public required string Type { get; init; }
    public required string OnDelete { get; init; }

    public static RelationApiResponse FromMetadata(RelationMetadata relation) => new()
    {
        Id = relation.RelationId,
        Name = relation.LogicalName,
        SourceTableId = relation.SourceTableId,
        SourceColumnId = relation.SourceColumnId,
        TargetTableId = relation.TargetTableId,
        TargetColumnId = relation.TargetColumnId,
        Type = relation.RelationType.ToString().ToLowerInvariant(),
        OnDelete = relation.OnDelete.ToString().ToLowerInvariant()
    };
}

#endregion

#region Data API Models

/// <summary>
/// Query parameters for data list endpoint.
/// </summary>
public sealed record DataQueryParameters
{
    /// <summary>
    /// Comma-separated list of columns to select.
    /// </summary>
    public string? Select { get; init; }

    /// <summary>
    /// Filter expression (column:op:value format).
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>
    /// Order by columns (column:asc or column:desc).
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size (default: 50, max: 1000).
    /// </summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>
/// Data record response with ID.
/// </summary>
public sealed record DataRecordResponse
{
    public Guid Id { get; init; }
    public required IDictionary<string, object?> Data { get; init; }
}

#endregion

#region Batch API Models

/// <summary>
/// Batch operation request.
/// </summary>
public sealed record BatchRequest
{
    public required IReadOnlyList<BatchOperation> Operations { get; init; }
}

/// <summary>
/// Single operation in a batch.
/// </summary>
public sealed record BatchOperation
{
    public required string Method { get; init; } // INSERT, UPDATE, DELETE, UPSERT
    public required string Table { get; init; }
    public Guid? Id { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
    public string? Filter { get; init; }
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Batch operation response.
/// </summary>
public sealed record BatchResponse
{
    public required IReadOnlyList<BatchOperationResult> Results { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}

/// <summary>
/// Result of a single batch operation.
/// </summary>
public sealed record BatchOperationResult
{
    public int Index { get; init; }
    public bool Success { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
    public string? Error { get; init; }
    public int? AffectedRows { get; init; }
}

#endregion

#region Helper Extensions

public static class ApiModelExtensions
{
    public static MorphDataType ParseDataType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "text" or "string" => MorphDataType.Text,
            "longtext" => MorphDataType.LongText,
            "integer" or "int" => MorphDataType.Integer,
            "biginteger" or "bigint" or "long" => MorphDataType.BigInteger,
            "decimal" or "number" or "float" or "double" => MorphDataType.Decimal,
            "boolean" or "bool" => MorphDataType.Boolean,
            "date" => MorphDataType.Date,
            "datetime" or "timestamp" => MorphDataType.DateTime,
            "time" => MorphDataType.Time,
            "uuid" or "guid" => MorphDataType.Uuid,
            "json" or "jsonb" => MorphDataType.Json,
            "array" => MorphDataType.Array,
            "email" => MorphDataType.Email,
            "url" => MorphDataType.Url,
            "phone" => MorphDataType.Phone,
            _ => Enum.TryParse<MorphDataType>(type, ignoreCase: true, out var result)
                ? result
                : throw new ArgumentException($"Unknown data type: {type}")
        };
    }

    public static IndexType ParseIndexType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "btree" or "b-tree" => IndexType.BTree,
            "hash" => IndexType.Hash,
            "gist" => IndexType.GiST,
            "gin" => IndexType.GIN,
            "brin" => IndexType.BRIN,
            _ => IndexType.BTree
        };
    }

    public static RelationType ParseRelationType(string type)
    {
        return type.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "onetoone" => RelationType.OneToOne,
            "onetomany" => RelationType.OneToMany,
            "manytomany" => RelationType.ManyToMany,
            _ => RelationType.OneToMany
        };
    }

    public static OnDeleteAction ParseOnDeleteAction(string action)
    {
        return action.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "noaction" => OnDeleteAction.NoAction,
            "cascade" => OnDeleteAction.Cascade,
            "setnull" => OnDeleteAction.SetNull,
            "setdefault" => OnDeleteAction.SetDefault,
            "restrict" => OnDeleteAction.Restrict,
            _ => OnDeleteAction.NoAction
        };
    }

    public static FilterOperator ParseFilterOperator(string op)
    {
        return op.ToLowerInvariant() switch
        {
            "eq" or "=" or "==" => FilterOperator.Equals,
            "neq" or "!=" or "<>" => FilterOperator.NotEquals,
            "gt" or ">" => FilterOperator.GreaterThan,
            "gte" or ">=" => FilterOperator.GreaterThanOrEquals,
            "lt" or "<" => FilterOperator.LessThan,
            "lte" or "<=" => FilterOperator.LessThanOrEquals,
            "like" => FilterOperator.Like,
            "ilike" => FilterOperator.ILike,
            "contains" => FilterOperator.Contains,
            "startswith" => FilterOperator.StartsWith,
            "endswith" => FilterOperator.EndsWith,
            _ => FilterOperator.Equals
        };
    }
}

#endregion
