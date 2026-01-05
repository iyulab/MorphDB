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

    /// <summary>
    /// Configuration for system columns. If null, defaults are applied:
    /// - Core columns (_id, _created_at, _updated_at): Always enabled
    /// - Versioning (_version): Enabled by default
    /// - Other columns: Disabled by default
    /// </summary>
    public SystemColumnOptionsApiRequest? SystemColumns { get; init; }
}

/// <summary>
/// Configuration options for system columns in API requests.
/// </summary>
public sealed record SystemColumnOptionsApiRequest
{
    /// <summary>
    /// Enable _version column for optimistic locking. Default: true.
    /// </summary>
    public bool Versioning { get; init; } = true;

    /// <summary>
    /// Enable _created_by and _updated_by columns. Default: false.
    /// </summary>
    public bool AuditFields { get; init; }

    /// <summary>
    /// Enable _deleted_at and _deleted_by for soft delete. Default: false.
    /// </summary>
    public bool SoftDelete { get; init; }

    /// <summary>
    /// Enable _owner_id for row-level ownership. Default: false.
    /// </summary>
    public bool Ownership { get; init; }

    /// <summary>
    /// Enable _parent_id and _sort_order for hierarchical data. Default: false.
    /// </summary>
    public bool Hierarchy { get; init; }

    /// <summary>
    /// Enable _source_id for external system tracking. Default: false.
    /// </summary>
    public bool SourceTracking { get; init; }

    /// <summary>
    /// Enable _row_state and _row_errors for draft mode and deferred validation. Default: false.
    /// </summary>
    public bool RowState { get; init; }

    /// <summary>
    /// Converts API model to Core SystemColumnOptions.
    /// </summary>
    public SystemColumnOptions ToOptions() => new()
    {
        VersioningEnabled = Versioning,
        AuditFieldsEnabled = AuditFields,
        SoftDeleteEnabled = SoftDelete,
        OwnershipEnabled = Ownership,
        HierarchyEnabled = Hierarchy,
        SourceTrackingEnabled = SourceTracking,
        RowStateEnabled = RowState
    };
}

/// <summary>
/// System column configuration in API responses.
/// </summary>
public sealed record SystemColumnOptionsApiResponse
{
    /// <summary>
    /// Whether timestamps (_created_at, _updated_at) are auto-managed.
    /// </summary>
    public bool Timestamps { get; init; }

    /// <summary>
    /// Whether _version column for optimistic locking is enabled.
    /// </summary>
    public bool Versioning { get; init; }

    /// <summary>
    /// Whether _created_by and _updated_by columns are enabled.
    /// </summary>
    public bool AuditFields { get; init; }

    /// <summary>
    /// Whether _deleted_at and _deleted_by for soft delete are enabled.
    /// </summary>
    public bool SoftDelete { get; init; }

    /// <summary>
    /// Whether _owner_id for row-level ownership is enabled.
    /// </summary>
    public bool Ownership { get; init; }

    /// <summary>
    /// Whether _parent_id and _sort_order for hierarchical data are enabled.
    /// </summary>
    public bool Hierarchy { get; init; }

    /// <summary>
    /// Whether _source_id for external system tracking is enabled.
    /// </summary>
    public bool SourceTracking { get; init; }

    /// <summary>
    /// Whether _row_state and _row_errors for draft mode are enabled.
    /// </summary>
    public bool RowState { get; init; }

    /// <summary>
    /// Creates response from TableMetadata.
    /// </summary>
    public static SystemColumnOptionsApiResponse FromMetadata(TableMetadata table) => new()
    {
        Timestamps = table.TimestampsEnabled,
        Versioning = table.VersioningEnabled,
        AuditFields = table.AuditFieldsEnabled,
        SoftDelete = table.SoftDeleteEnabled,
        Ownership = table.OwnershipEnabled,
        Hierarchy = table.HierarchyEnabled,
        SourceTracking = table.SourceTrackingEnabled,
        RowState = table.RowStateEnabled
    };
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

    /// <summary>
    /// Configuration for lookup fields that reference related table data.
    /// When set, creates a virtual lookup column instead of a physical column.
    /// </summary>
    public LookupConfigApiRequest? Lookup { get; init; }

    /// <summary>
    /// Configuration for rollup fields that aggregate data from related tables.
    /// When set, creates a virtual rollup column instead of a physical column.
    /// </summary>
    public RollupConfigApiRequest? Rollup { get; init; }

    /// <summary>
    /// Configuration for formula fields that compute values from expressions.
    /// When set, creates a virtual formula column instead of a physical column.
    /// </summary>
    public FormulaConfigApiRequest? Formula { get; init; }
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

    /// <summary>
    /// Configuration for lookup fields that reference related table data.
    /// When set, creates a virtual lookup column instead of a physical column.
    /// </summary>
    public LookupConfigApiRequest? Lookup { get; init; }

    /// <summary>
    /// Configuration for rollup fields that aggregate data from related tables.
    /// When set, creates a virtual rollup column instead of a physical column.
    /// </summary>
    public RollupConfigApiRequest? Rollup { get; init; }

    /// <summary>
    /// Configuration for formula fields that compute values from expressions.
    /// When set, creates a virtual formula column instead of a physical column.
    /// </summary>
    public FormulaConfigApiRequest? Formula { get; init; }
}

/// <summary>
/// Configuration for lookup fields in API requests.
/// </summary>
public sealed record LookupConfigApiRequest
{
    /// <summary>
    /// The relation column in this table (foreign key column name).
    /// </summary>
    public required string RelationColumn { get; init; }

    /// <summary>
    /// The target table to look up from.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The column to retrieve from the target table.
    /// </summary>
    public required string TargetColumn { get; init; }

    /// <summary>
    /// Action when the referenced record is deleted: set-null, preserve, or clear.
    /// Default: set-null.
    /// </summary>
    public string OnDelete { get; init; } = "set-null";

    /// <summary>
    /// Whether to support multiple values (when relation is one-to-many).
    /// </summary>
    public bool AllowMultiple { get; init; }

    /// <summary>
    /// Converts to core LookupColumnConfig model.
    /// </summary>
    public LookupColumnConfig ToModel() => new()
    {
        RelationColumn = RelationColumn,
        TargetTable = TargetTable,
        TargetColumn = TargetColumn,
        OnDelete = ParseOnDeleteAction(OnDelete),
        AllowMultiple = AllowMultiple
    };

    private static LookupDeleteAction ParseOnDeleteAction(string action)
    {
        return action.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "setnull" => LookupDeleteAction.SetNull,
            "preserve" => LookupDeleteAction.Preserve,
            "clear" => LookupDeleteAction.Clear,
            _ => LookupDeleteAction.SetNull
        };
    }
}

/// <summary>
/// Configuration for rollup fields in API requests.
/// </summary>
public sealed record RollupConfigApiRequest
{
    /// <summary>
    /// The relation that connects to the records to roll up.
    /// </summary>
    public required string Relation { get; init; }

    /// <summary>
    /// The table containing the records to roll up (child table).
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The foreign key column in the target table that references this table.
    /// </summary>
    public required string ForeignKeyColumn { get; init; }

    /// <summary>
    /// The column in the target table to aggregate.
    /// Use "*" for COUNT operations.
    /// </summary>
    public string SourceColumn { get; init; } = "*";

    /// <summary>
    /// The aggregation function: count, count-values, count-empty, sum, average, min, max,
    /// string-concat, array-values, percent-checked, percent-unchecked,
    /// earliest-date, latest-date, date-range, all-true, any-true.
    /// </summary>
    public required string Aggregation { get; init; }

    /// <summary>
    /// Optional filter to apply before aggregation.
    /// </summary>
    public RollupFilterApiRequest? Filter { get; init; }

    /// <summary>
    /// Delimiter for string-concat aggregation. Default: ", ".
    /// </summary>
    public string? Delimiter { get; init; }

    /// <summary>
    /// Order by expression for string-concat and array-values aggregations.
    /// </summary>
    public string? OrderBy { get; init; }

    /// <summary>
    /// Converts to core RollupColumnConfig model.
    /// </summary>
    public RollupColumnConfig ToModel() => new()
    {
        Relation = Relation,
        TargetTable = TargetTable,
        ForeignKeyColumn = ForeignKeyColumn,
        SourceColumn = SourceColumn,
        Aggregation = ParseRollupAggregation(Aggregation),
        Filter = Filter?.ToModel(),
        Delimiter = Delimiter,
        OrderBy = OrderBy
    };

    private static RollupAggregation ParseRollupAggregation(string aggregation)
    {
        return aggregation.ToLowerInvariant().Replace("-", "").Replace("_", "") switch
        {
            "count" => RollupAggregation.Count,
            "countvalues" => RollupAggregation.CountValues,
            "countempty" => RollupAggregation.CountEmpty,
            "sum" => RollupAggregation.Sum,
            "average" or "avg" => RollupAggregation.Average,
            "min" => RollupAggregation.Min,
            "max" => RollupAggregation.Max,
            "stringconcat" or "concat" => RollupAggregation.StringConcat,
            "arrayvalues" or "array" => RollupAggregation.ArrayValues,
            "percentchecked" => RollupAggregation.PercentChecked,
            "percentunchecked" => RollupAggregation.PercentUnchecked,
            "earliestdate" or "earliest" => RollupAggregation.EarliestDate,
            "latestdate" or "latest" => RollupAggregation.LatestDate,
            "daterange" => RollupAggregation.DateRange,
            "alltrue" or "all" => RollupAggregation.AllTrue,
            "anytrue" or "any" => RollupAggregation.AnyTrue,
            _ => RollupAggregation.Count
        };
    }
}

/// <summary>
/// Filter configuration for rollup operations in API requests.
/// </summary>
public sealed record RollupFilterApiRequest
{
    /// <summary>
    /// The field to filter on.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// The comparison operator: eq, neq, gt, gte, lt, lte, contains, starts-with, ends-with, is-null, is-not-null, in, not-in.
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Converts to core RollupFilter model.
    /// </summary>
    public RollupFilter ToModel() => new()
    {
        Field = Field,
        Operator = ApiModelExtensions.ParseFilterOperator(Operator),
        Value = Value
    };
}

/// <summary>
/// Configuration for formula fields in API requests.
/// </summary>
public sealed record FormulaConfigApiRequest
{
    /// <summary>
    /// The formula expression in MorphDB formula syntax.
    /// Supports column references like {field_name}, operators (+, -, *, /),
    /// and functions (IF, CONCAT, UPPER, LOWER, NOW, etc.).
    /// </summary>
    public required string Formula { get; init; }

    /// <summary>
    /// Optional explicit return type. If not specified, type is inferred from the formula.
    /// </summary>
    public string? ReturnType { get; init; }

    /// <summary>
    /// Format string for output (e.g., currency, percentage).
    /// </summary>
    public string? OutputFormat { get; init; }

    /// <summary>
    /// Converts to core FormulaColumnConfig model.
    /// </summary>
    public FormulaColumnConfig ToModel() => new()
    {
        Formula = Formula,
        ReturnType = ReturnType != null ? ApiModelExtensions.ParseDataType(ReturnType) : MorphDataType.Text,
        OutputFormat = OutputFormat
    };
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

    /// <summary>
    /// System column configuration for this table.
    /// </summary>
    public SystemColumnOptionsApiResponse SystemColumns { get; init; } = new();

    public static TableApiResponse FromMetadata(TableMetadata table) => new()
    {
        Id = table.TableId,
        Name = table.LogicalName,
        Version = table.SchemaVersion,
        CreatedAt = table.CreatedAt,
        UpdatedAt = table.UpdatedAt,
        Columns = table.Columns.Select(ColumnApiResponse.FromMetadata).ToList(),
        Indexes = table.Indexes.Select(IndexApiResponse.FromMetadata).ToList(),
        Relations = table.Relations.Select(RelationApiResponse.FromMetadata).ToList(),
        SystemColumns = SystemColumnOptionsApiResponse.FromMetadata(table)
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

    /// <summary>
    /// Whether this is a derived/virtual column (lookup, rollup, formula).
    /// </summary>
    public bool IsDerived { get; init; }

    /// <summary>
    /// Lookup configuration if this is a lookup column.
    /// </summary>
    public LookupConfigApiResponse? Lookup { get; init; }

    /// <summary>
    /// Rollup configuration if this is a rollup column.
    /// </summary>
    public RollupConfigApiResponse? Rollup { get; init; }

    /// <summary>
    /// Formula configuration if this is a formula column.
    /// </summary>
    public FormulaConfigApiResponse? Formula { get; init; }

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
        Position = column.OrdinalPosition,
        IsDerived = column.IsDerived,
        Lookup = column.LookupConfig != null
            ? LookupConfigApiResponse.FromModel(column.LookupConfig)
            : null,
        Rollup = column.RollupConfig != null
            ? RollupConfigApiResponse.FromModel(column.RollupConfig)
            : null,
        Formula = column.FormulaConfig != null
            ? FormulaConfigApiResponse.FromModel(column.FormulaConfig)
            : null
    };
}

/// <summary>
/// Lookup configuration in API responses.
/// </summary>
public sealed record LookupConfigApiResponse
{
    /// <summary>
    /// The relation column in this table (foreign key column name).
    /// </summary>
    public required string RelationColumn { get; init; }

    /// <summary>
    /// The target table to look up from.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The column to retrieve from the target table.
    /// </summary>
    public required string TargetColumn { get; init; }

    /// <summary>
    /// Action when the referenced record is deleted.
    /// </summary>
    public required string OnDelete { get; init; }

    /// <summary>
    /// Whether multiple values are supported.
    /// </summary>
    public bool AllowMultiple { get; init; }

    public static LookupConfigApiResponse FromModel(LookupColumnConfig config) => new()
    {
        RelationColumn = config.RelationColumn,
        TargetTable = config.TargetTable,
        TargetColumn = config.TargetColumn,
        OnDelete = config.OnDelete.ToString().ToLowerInvariant(),
        AllowMultiple = config.AllowMultiple
    };
}

/// <summary>
/// Rollup configuration in API responses.
/// </summary>
public sealed record RollupConfigApiResponse
{
    /// <summary>
    /// The relation that connects to the records to roll up.
    /// </summary>
    public required string Relation { get; init; }

    /// <summary>
    /// The target table containing the records to roll up.
    /// </summary>
    public required string TargetTable { get; init; }

    /// <summary>
    /// The foreign key column in the target table.
    /// </summary>
    public required string ForeignKeyColumn { get; init; }

    /// <summary>
    /// The column being aggregated.
    /// </summary>
    public required string SourceColumn { get; init; }

    /// <summary>
    /// The aggregation function being applied.
    /// </summary>
    public required string Aggregation { get; init; }

    /// <summary>
    /// Optional filter applied before aggregation.
    /// </summary>
    public RollupFilterApiResponse? Filter { get; init; }

    /// <summary>
    /// Delimiter for string concatenation.
    /// </summary>
    public string? Delimiter { get; init; }

    /// <summary>
    /// Order by expression for ordered aggregations.
    /// </summary>
    public string? OrderBy { get; init; }

    public static RollupConfigApiResponse FromModel(RollupColumnConfig config) => new()
    {
        Relation = config.Relation,
        TargetTable = config.TargetTable,
        ForeignKeyColumn = config.ForeignKeyColumn,
        SourceColumn = config.SourceColumn,
        Aggregation = FormatAggregation(config.Aggregation),
        Filter = config.Filter != null ? RollupFilterApiResponse.FromModel(config.Filter) : null,
        Delimiter = config.Delimiter,
        OrderBy = config.OrderBy
    };

    private static string FormatAggregation(RollupAggregation aggregation)
    {
        return aggregation switch
        {
            RollupAggregation.Count => "count",
            RollupAggregation.CountValues => "count-values",
            RollupAggregation.CountEmpty => "count-empty",
            RollupAggregation.Sum => "sum",
            RollupAggregation.Average => "average",
            RollupAggregation.Min => "min",
            RollupAggregation.Max => "max",
            RollupAggregation.StringConcat => "string-concat",
            RollupAggregation.ArrayValues => "array-values",
            RollupAggregation.PercentChecked => "percent-checked",
            RollupAggregation.PercentUnchecked => "percent-unchecked",
            RollupAggregation.EarliestDate => "earliest-date",
            RollupAggregation.LatestDate => "latest-date",
            RollupAggregation.DateRange => "date-range",
            RollupAggregation.AllTrue => "all-true",
            RollupAggregation.AnyTrue => "any-true",
            _ => "count"
        };
    }
}

/// <summary>
/// Rollup filter configuration in API responses.
/// </summary>
public sealed record RollupFilterApiResponse
{
    /// <summary>
    /// The field being filtered.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// The comparison value.
    /// </summary>
    public object? Value { get; init; }

    public static RollupFilterApiResponse FromModel(RollupFilter filter) => new()
    {
        Field = filter.Field,
        Operator = FormatOperator(filter.Operator),
        Value = filter.Value
    };

    private static string FormatOperator(FilterOperator op)
    {
        return op switch
        {
            FilterOperator.Equals => "eq",
            FilterOperator.NotEquals => "neq",
            FilterOperator.GreaterThan => "gt",
            FilterOperator.GreaterThanOrEquals => "gte",
            FilterOperator.LessThan => "lt",
            FilterOperator.LessThanOrEquals => "lte",
            FilterOperator.Contains => "contains",
            FilterOperator.StartsWith => "starts-with",
            FilterOperator.EndsWith => "ends-with",
            FilterOperator.IsNull => "is-null",
            FilterOperator.IsNotNull => "is-not-null",
            FilterOperator.In => "in",
            FilterOperator.NotIn => "not-in",
            _ => "eq"
        };
    }
}

/// <summary>
/// Formula configuration in API responses.
/// </summary>
public sealed record FormulaConfigApiResponse
{
    /// <summary>
    /// The formula expression.
    /// </summary>
    public required string Formula { get; init; }

    /// <summary>
    /// The return type of the formula.
    /// </summary>
    public required string ReturnType { get; init; }

    /// <summary>
    /// Fields referenced by the formula.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>
    /// Whether the formula contains volatile functions (NOW(), TODAY(), etc.).
    /// </summary>
    public bool IsVolatile { get; init; }

    /// <summary>
    /// Format string for output (e.g., currency, percentage).
    /// </summary>
    public string? OutputFormat { get; init; }

    public static FormulaConfigApiResponse FromModel(FormulaColumnConfig config) => new()
    {
        Formula = config.Formula,
        ReturnType = config.ReturnType.ToString().ToLowerInvariant(),
        Dependencies = config.Dependencies,
        IsVolatile = config.IsVolatile,
        OutputFormat = config.OutputFormat
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
        Columns = index.Columns.Select(c => c.LogicalName).ToList(),
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

    /// <summary>
    /// Filter by row state: "draft", "valid", "error", or "all".
    /// Only applies to tables with RowStateEnabled.
    /// Default: "valid" (excludes draft and error rows).
    /// </summary>
    public string? State { get; init; }
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

#region Transaction API Models

/// <summary>
/// Cross-entity transaction request with $ref support.
/// </summary>
public sealed record TransactionApiRequest
{
    /// <summary>
    /// List of operations to execute atomically in order.
    /// </summary>
    public required IReadOnlyList<TransactionOperationApiRequest> Operations { get; init; }

    /// <summary>
    /// Optional timeout in milliseconds. Default: 30000 (30 seconds).
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// When true, returns full record data for each operation.
    /// </summary>
    public bool ReturnFullRecords { get; init; }
}

/// <summary>
/// Single operation within a transaction.
/// </summary>
public sealed record TransactionOperationApiRequest
{
    /// <summary>
    /// Operation type: INSERT, UPDATE, DELETE, or UPSERT.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// The table name to operate on.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// The data for INSERT/UPDATE/UPSERT operations.
    /// Values can contain $ref expressions like "$order._id".
    /// </summary>
    public IDictionary<string, object?>? Data { get; init; }

    /// <summary>
    /// Record ID for UPDATE/DELETE operations.
    /// Can be a GUID or a $ref expression like "$order._id".
    /// </summary>
    public object? Id { get; init; }

    /// <summary>
    /// Reference name for this operation's result.
    /// Other operations can reference using $[ref].[property].
    /// </summary>
    public string? Ref { get; init; }

    /// <summary>
    /// Key columns for UPSERT operations.
    /// </summary>
    public IReadOnlyList<string>? KeyColumns { get; init; }

    /// <summary>
    /// Write mode: "default" or "draft".
    /// </summary>
    public string? Mode { get; init; }
}

/// <summary>
/// Transaction result response.
/// </summary>
public sealed record TransactionApiResponse
{
    /// <summary>
    /// Whether the entire transaction succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Results for each operation in order.
    /// </summary>
    public required IReadOnlyList<TransactionOperationApiResult> Results { get; init; }

    /// <summary>
    /// Error message if the transaction failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Index of the operation that caused the failure.
    /// </summary>
    public int? FailedOperationIndex { get; init; }
}

/// <summary>
/// Result of a single operation within a transaction.
/// </summary>
public sealed record TransactionOperationApiResult
{
    public int Index { get; init; }
    public bool Success { get; init; }
    public string? Ref { get; init; }
    public Guid? Id { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
    public int AffectedRows { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<ValidationErrorApi>? ValidationErrors { get; init; }
}

/// <summary>
/// Validation error in API response.
/// </summary>
public sealed record ValidationErrorApi
{
    public required string Field { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// Request to finalize (validate) records.
/// </summary>
public sealed record FinalizeApiRequest
{
    /// <summary>
    /// Record IDs to finalize. If empty, finalizes a single record via URL parameter.
    /// </summary>
    public IReadOnlyList<Guid>? RecordIds { get; init; }
}

/// <summary>
/// Response for finalize operations.
/// </summary>
public sealed record FinalizeApiResponse
{
    public required IReadOnlyList<FinalizeResultApi> Results { get; init; }
    public int ValidCount { get; init; }
    public int ErrorCount { get; init; }
}

/// <summary>
/// Result of a single finalize operation.
/// </summary>
public sealed record FinalizeResultApi
{
    public Guid RecordId { get; init; }
    public bool Success { get; init; }
    public string NewState { get; init; } = "valid";
    public IReadOnlyList<RowValidationErrorApi>? Errors { get; init; }
    public IDictionary<string, object?>? Data { get; init; }
}

/// <summary>
/// Row validation error in API response.
/// </summary>
public sealed record RowValidationErrorApi
{
    public required string Column { get; init; }
    public required string Error { get; init; }
    public required string Message { get; init; }
    public object? AttemptedValue { get; init; }
}

#endregion

#region Webhook API Models

/// <summary>
/// Request to create a webhook.
/// </summary>
public sealed record CreateWebhookApiRequest
{
    public required string Name { get; init; }
    public required string Table { get; init; }
    public required string Url { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public System.Text.Json.JsonDocument? Filter { get; init; }
    public System.Text.Json.JsonDocument? Headers { get; init; }
}

/// <summary>
/// Request to update a webhook.
/// </summary>
public sealed record UpdateWebhookApiRequest
{
    public string? Url { get; init; }
    public IReadOnlyList<string>? Events { get; init; }
    public System.Text.Json.JsonDocument? Filter { get; init; }
    public System.Text.Json.JsonDocument? Headers { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>
/// Webhook API response.
/// </summary>
public sealed record WebhookApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Table { get; init; }
    public required string Url { get; init; }
    public string? Secret { get; init; }
    public required IReadOnlyList<string> Events { get; init; }
    public System.Text.Json.JsonDocument? Filter { get; init; }
    public System.Text.Json.JsonDocument? Headers { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Response for secret regeneration.
/// </summary>
public sealed record RegenerateSecretResponse
{
    public required string Secret { get; init; }
}

/// <summary>
/// Webhook delivery API response.
/// </summary>
public sealed record DeliveryApiResponse
{
    public Guid Id { get; init; }
    public required string Event { get; init; }
    public Guid? RecordId { get; init; }
    public required string Status { get; init; }
    public int AttemptCount { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? DeliveredAt { get; init; }
}

/// <summary>
/// Dead Letter Queue message API response.
/// </summary>
public sealed record DlqMessageApiResponse
{
    public Guid DlqId { get; init; }
    public Guid DeliveryId { get; init; }
    public Guid WebhookId { get; init; }
    public Guid? RecordId { get; init; }
    public required string Event { get; init; }
    public required string Reason { get; init; }
    public int AttemptCount { get; init; }
    public int? LastHttpStatusCode { get; init; }
    public string? LastErrorMessage { get; init; }
    public required string Status { get; init; }
    public string? ResolutionNotes { get; init; }
    public DateTimeOffset DlqAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}

/// <summary>
/// DLQ statistics API response.
/// </summary>
public sealed record DlqStatisticsApiResponse
{
    public int TotalMessages { get; init; }
    public int PendingReviewCount { get; init; }
    public int ResolvedCount { get; init; }
    public int ArchivedCount { get; init; }
    public IReadOnlyDictionary<string, int> ByReason { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<Guid, int> ByWebhook { get; init; } = new Dictionary<Guid, int>();
    public DateTimeOffset? OldestPendingAt { get; init; }
}

/// <summary>
/// Request to resolve a DLQ message.
/// </summary>
public sealed record ResolveDlqApiRequest
{
    public required string ResolutionNotes { get; init; }
    public Guid? ResolvedBy { get; init; }
}

/// <summary>
/// Response for DLQ archive operation.
/// </summary>
public sealed record ArchiveDlqApiResponse
{
    public int ArchivedCount { get; init; }
}

#endregion

#region Bulk Import/Export API Models

/// <summary>
/// Request to start a CSV import.
/// </summary>
public sealed record CsvImportApiRequest
{
    public char Delimiter { get; init; } = ',';
    public bool HasHeader { get; init; } = true;
    public string? DateFormat { get; init; }
    public bool TrimWhitespace { get; init; } = true;
    public string NullHandling { get; init; } = "empty-as-null";
    public string DuplicateHandling { get; init; } = "insert";
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Request to start a JSON import.
/// </summary>
public sealed record JsonImportApiRequest
{
    public string? DateFormat { get; init; }
    public string DuplicateHandling { get; init; } = "insert";
    public IReadOnlyList<string>? KeyColumns { get; init; }
}

/// <summary>
/// Request to start a CSV export.
/// </summary>
public sealed record CsvExportApiRequest
{
    public char Delimiter { get; init; } = ',';
    public bool IncludeHeader { get; init; } = true;
    public string? DateFormat { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// Request to start a JSON export.
/// </summary>
public sealed record JsonExportApiRequest
{
    public bool Pretty { get; init; }
    public string? DateFormat { get; init; }
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// Request to start an XLSX export.
/// </summary>
public sealed record XlsxExportApiRequest
{
    public string SheetName { get; init; } = "Data";
    public bool IncludeHeader { get; init; } = true;
    public IReadOnlyList<string>? Columns { get; init; }
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
}

/// <summary>
/// Import job API response.
/// </summary>
public sealed record ImportJobApiResponse
{
    public Guid JobId { get; init; }
    public required string TableName { get; init; }
    public required string Format { get; init; }
    public required string Status { get; init; }
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public double PercentComplete => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
}

/// <summary>
/// Export job API response.
/// </summary>
public sealed record ExportJobApiResponse
{
    public Guid JobId { get; init; }
    public required string TableName { get; init; }
    public required string Format { get; init; }
    public required string Status { get; init; }
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public long? FileSize { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public double PercentComplete => TotalRows > 0 ? (double)ProcessedRows / TotalRows * 100 : 0;
}

/// <summary>
/// Job progress API response.
/// </summary>
public sealed record JobProgressApiResponse
{
    public Guid JobId { get; init; }
    public required string Status { get; init; }
    public long TotalRows { get; init; }
    public long ProcessedRows { get; init; }
    public long SuccessCount { get; init; }
    public long ErrorCount { get; init; }
    public double PercentComplete { get; init; }
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}

#endregion

#region Project API Models

/// <summary>
/// Request to create a new project.
/// </summary>
public sealed record CreateProjectApiRequest
{
    /// <summary>
    /// Human-readable project name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe unique identifier. If null, will be generated from name.
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Optional organization ID for hierarchical multi-tenancy.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Optional project settings.
    /// </summary>
    public ProjectSettingsApiModel? Settings { get; init; }
}

/// <summary>
/// Request to update an existing project.
/// </summary>
public sealed record UpdateProjectApiRequest
{
    /// <summary>
    /// New project name (optional).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Updated settings (optional). Null means no change.
    /// </summary>
    public ProjectSettingsApiModel? Settings { get; init; }
}

/// <summary>
/// Project settings API model.
/// </summary>
public sealed record ProjectSettingsApiModel
{
    public string? DefaultLocale { get; init; }
    public string? Timezone { get; init; }
    public bool EnableAuditLog { get; init; } = true;
    public int? MaxTables { get; init; }
    public long? MaxStorageBytes { get; init; }
    public RateLimitSettingsApiModel? RateLimits { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }

    public static ProjectSettingsApiModel? FromModel(ProjectSettings? settings)
    {
        if (settings is null)
            return null;
        return new ProjectSettingsApiModel
        {
            DefaultLocale = settings.DefaultLocale,
            Timezone = settings.Timezone,
            EnableAuditLog = settings.EnableAuditLog,
            MaxTables = settings.MaxTables,
            MaxStorageBytes = settings.MaxStorageBytes,
            RateLimits = RateLimitSettingsApiModel.FromModel(settings.RateLimits),
            Metadata = settings.Metadata
        };
    }

    public ProjectSettings ToModel() => new()
    {
        DefaultLocale = DefaultLocale,
        Timezone = Timezone,
        EnableAuditLog = EnableAuditLog,
        MaxTables = MaxTables,
        MaxStorageBytes = MaxStorageBytes,
        RateLimits = RateLimits?.ToModel(),
        Metadata = Metadata
    };
}

/// <summary>
/// Rate limit settings API model.
/// </summary>
public sealed record RateLimitSettingsApiModel
{
    public int? RequestsPerMinute { get; init; }
    public int? RequestsPerHour { get; init; }
    public int? MaxConcurrentConnections { get; init; }

    public static RateLimitSettingsApiModel? FromModel(RateLimitSettings? settings)
    {
        if (settings is null)
            return null;
        return new RateLimitSettingsApiModel
        {
            RequestsPerMinute = settings.RequestsPerMinute,
            RequestsPerHour = settings.RequestsPerHour,
            MaxConcurrentConnections = settings.MaxConcurrentConnections
        };
    }

    public RateLimitSettings ToModel() => new()
    {
        RequestsPerMinute = RequestsPerMinute,
        RequestsPerHour = RequestsPerHour,
        MaxConcurrentConnections = MaxConcurrentConnections
    };
}

/// <summary>
/// Project API response.
/// </summary>
public sealed record ProjectApiResponse
{
    public Guid Id { get; init; }
    public Guid? OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
    public required string SystemSchema { get; init; }
    public required string DataSchema { get; init; }
    public required string Status { get; init; }
    public ProjectSettingsApiModel? Settings { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static ProjectApiResponse FromModel(Project project) => new()
    {
        Id = project.ProjectId,
        OrganizationId = project.OrganizationId,
        Name = project.Name,
        Slug = project.Slug,
        SystemSchema = project.SystemSchema,
        DataSchema = project.DataSchema,
        Status = project.Status.ToString().ToLowerInvariant(),
        Settings = ProjectSettingsApiModel.FromModel(project.Settings),
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };
}

/// <summary>
/// Project statistics API response.
/// </summary>
public sealed record ProjectStatsApiResponse
{
    public Guid ProjectId { get; init; }
    public required SchemaStatsApiResponse SystemSchemaStats { get; init; }
    public required SchemaStatsApiResponse DataSchemaStats { get; init; }
    public long TotalSizeBytes { get; init; }
    public int TotalTableCount { get; init; }

    public static ProjectStatsApiResponse FromModel(ProjectSchemaStats stats) => new()
    {
        ProjectId = stats.ProjectId,
        SystemSchemaStats = SchemaStatsApiResponse.FromModel(stats.SystemSchemaStats),
        DataSchemaStats = SchemaStatsApiResponse.FromModel(stats.DataSchemaStats),
        TotalSizeBytes = stats.TotalSizeBytes,
        TotalTableCount = stats.TotalTableCount
    };
}

/// <summary>
/// Schema statistics API response.
/// </summary>
public sealed record SchemaStatsApiResponse
{
    public required string SchemaName { get; init; }
    public int TableCount { get; init; }
    public int IndexCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public long DataSizeBytes { get; init; }
    public long IndexSizeBytes { get; init; }
    public DateTimeOffset? LastModified { get; init; }

    public static SchemaStatsApiResponse FromModel(SchemaStats stats) => new()
    {
        SchemaName = stats.SchemaName,
        TableCount = stats.TableCount,
        IndexCount = stats.IndexCount,
        TotalSizeBytes = stats.TotalSizeBytes,
        DataSizeBytes = stats.DataSizeBytes,
        IndexSizeBytes = stats.IndexSizeBytes,
        LastModified = stats.LastModified
    };
}

/// <summary>
/// Schema health report API response.
/// </summary>
public sealed record SchemaHealthApiResponse
{
    public Guid ProjectId { get; init; }
    public bool IsHealthy { get; init; }
    public required IReadOnlyList<SchemaHealthIssueApiResponse> Issues { get; init; }
    public DateTimeOffset CheckedAt { get; init; }

    public static SchemaHealthApiResponse FromModel(SchemaHealthReport report) => new()
    {
        ProjectId = report.ProjectId,
        IsHealthy = report.IsHealthy,
        Issues = report.Issues.Select(SchemaHealthIssueApiResponse.FromModel).ToList(),
        CheckedAt = report.CheckedAt
    };
}

/// <summary>
/// Schema health issue API response.
/// </summary>
public sealed record SchemaHealthIssueApiResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public string? AffectedObject { get; init; }

    public static SchemaHealthIssueApiResponse FromModel(SchemaHealthIssue issue) => new()
    {
        Code = issue.Code,
        Message = issue.Message,
        Severity = issue.Severity.ToString().ToLowerInvariant(),
        AffectedObject = issue.AffectedObject
    };
}

/// <summary>
/// Query parameters for project list endpoint.
/// </summary>
public sealed record ProjectQueryParameters
{
    /// <summary>
    /// Filter by organization ID.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Filter by status.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size (default: 50, max: 100).
    /// </summary>
    public int PageSize { get; init; } = 50;
}

#endregion

#region Aggregation API Models

/// <summary>
/// Request to perform aggregation on a table.
/// </summary>
public sealed record AggregationApiRequest
{
    /// <summary>
    /// Aggregation columns to compute (COUNT, SUM, AVG, MIN, MAX).
    /// </summary>
    public required IReadOnlyList<AggregationColumnApiRequest> Aggregations { get; init; }

    /// <summary>
    /// Columns to group by (logical names).
    /// </summary>
    public IReadOnlyList<string> GroupBy { get; init; } = [];

    /// <summary>
    /// Filter conditions (applied before grouping).
    /// </summary>
    public IReadOnlyList<FilterConditionApiRequest>? Filter { get; init; }

    /// <summary>
    /// Having conditions (applied after grouping).
    /// </summary>
    public IReadOnlyList<HavingConditionApiRequest>? Having { get; init; }

    /// <summary>
    /// Order by specifications.
    /// </summary>
    public IReadOnlyList<AggregationOrderByApiRequest>? OrderBy { get; init; }

    /// <summary>
    /// Maximum number of groups to return.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Number of groups to skip.
    /// </summary>
    public int? Offset { get; init; }

    /// <summary>
    /// Converts to core AggregationRequest model.
    /// </summary>
    public AggregationRequest ToModel() => new()
    {
        Aggregations = Aggregations.Select(a => a.ToModel()).ToList(),
        GroupBy = GroupBy,
        Filter = Filter?.Select(f => f.ToModel()).ToList(),
        Having = Having?.Select(h => h.ToModel()).ToList(),
        OrderBy = OrderBy?.Select(o => o.ToModel()).ToList(),
        Limit = Limit,
        Offset = Offset
    };
}

/// <summary>
/// Aggregation column specification.
/// </summary>
public sealed record AggregationColumnApiRequest
{
    /// <summary>
    /// Aggregation function: count, sum, avg, min, max.
    /// </summary>
    public required string Function { get; init; }

    /// <summary>
    /// Column to aggregate (null for COUNT(*)).
    /// </summary>
    public string? Column { get; init; }

    /// <summary>
    /// Alias for the result column.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Whether to use DISTINCT.
    /// </summary>
    public bool Distinct { get; init; }

    /// <summary>
    /// Converts to core AggregationColumn model.
    /// </summary>
    public AggregationColumn ToModel() => new()
    {
        Function = ParseAggregateFunction(Function),
        Column = Column,
        Alias = Alias,
        Distinct = Distinct
    };

    private static AggregateFunction ParseAggregateFunction(string function)
    {
        return function.ToLowerInvariant() switch
        {
            "count" => AggregateFunction.Count,
            "countdistinct" or "count_distinct" or "count-distinct" => AggregateFunction.CountDistinct,
            "sum" => AggregateFunction.Sum,
            "avg" or "average" => AggregateFunction.Avg,
            "min" => AggregateFunction.Min,
            "max" => AggregateFunction.Max,
            _ => throw new ArgumentException($"Unknown aggregate function: {function}")
        };
    }
}

/// <summary>
/// Filter condition for aggregation WHERE clause.
/// </summary>
public sealed record FilterConditionApiRequest
{
    /// <summary>
    /// Column to filter on (logical name).
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Filter operator: eq, neq, gt, gte, lt, lte, like, ilike, contains, starts-with, ends-with, in, not-in, is-null, is-not-null, between.
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Converts to core FilterCondition model.
    /// </summary>
    public FilterCondition ToModel() => new()
    {
        Column = Column,
        Operator = ApiModelExtensions.ParseFilterOperator(Operator),
        Value = Value
    };
}

/// <summary>
/// Having condition for aggregation results.
/// </summary>
public sealed record HavingConditionApiRequest
{
    /// <summary>
    /// Aggregation alias to filter on.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Comparison operator: eq, neq, gt, gte, lt, lte.
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// Converts to core HavingCondition model.
    /// </summary>
    public HavingCondition ToModel() => new()
    {
        Alias = Alias,
        Operator = ApiModelExtensions.ParseFilterOperator(Operator),
        Value = Value
    };
}

/// <summary>
/// Order by specification for aggregation results.
/// </summary>
public sealed record AggregationOrderByApiRequest
{
    /// <summary>
    /// Column name or aggregation alias.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Sort direction: asc or desc.
    /// </summary>
    public string Direction { get; init; } = "asc";

    /// <summary>
    /// Converts to core AggregationOrderBy model.
    /// </summary>
    public AggregationOrderBy ToModel() => new()
    {
        Column = Column,
        Descending = Direction.Equals("desc", StringComparison.OrdinalIgnoreCase)
    };
}

/// <summary>
/// Aggregation result response.
/// </summary>
public sealed record AggregationApiResponse
{
    /// <summary>
    /// Aggregated data rows.
    /// </summary>
    public required IReadOnlyList<IDictionary<string, object?>> Data { get; init; }

    /// <summary>
    /// Total number of groups (before limit/offset).
    /// </summary>
    public long? TotalGroups { get; init; }

    /// <summary>
    /// Execution metadata.
    /// </summary>
    public AggregationMetadataApiResponse? Metadata { get; init; }

    public static AggregationApiResponse FromResult(AggregationResult result) => new()
    {
        Data = result.Data,
        TotalGroups = result.TotalGroups,
        Metadata = result.Metadata != null
            ? AggregationMetadataApiResponse.FromModel(result.Metadata)
            : null
    };
}

/// <summary>
/// Aggregation execution metadata.
/// </summary>
public sealed record AggregationMetadataApiResponse
{
    /// <summary>
    /// Number of rows scanned.
    /// </summary>
    public long RowsScanned { get; init; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; init; }

    public static AggregationMetadataApiResponse FromModel(AggregationMetadata metadata) => new()
    {
        RowsScanned = metadata.RowsScanned,
        ExecutionTimeMs = metadata.ExecutionTimeMs
    };
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

#region View API Models

/// <summary>
/// Request to create a new view.
/// </summary>
public sealed record CreateViewApiRequest
{
    /// <summary>
    /// User-facing name for the view.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Primary base table for the view.
    /// </summary>
    public required string BaseTable { get; init; }

    /// <summary>
    /// Columns to include in the view.
    /// </summary>
    public IReadOnlyList<ViewColumnApiSpec> Columns { get; init; } = [];

    /// <summary>
    /// Additional tables joined to the base table.
    /// </summary>
    public IReadOnlyList<ViewJoinApiSpec>? Joins { get; init; }

    /// <summary>
    /// Filter conditions applied to the view.
    /// </summary>
    public IReadOnlyList<ViewFilterApiSpec>? Filters { get; init; }

    /// <summary>
    /// Group by columns for aggregate views.
    /// </summary>
    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>
    /// Default ordering for view results.
    /// </summary>
    public IReadOnlyList<ViewOrderApiSpec>? OrderBy { get; init; }

    /// <summary>
    /// Maximum number of rows.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Whether to include only distinct rows.
    /// </summary>
    public bool Distinct { get; init; }

    /// <summary>
    /// When true, create a materialized view.
    /// </summary>
    public bool Materialized { get; init; }

    /// <summary>
    /// Refresh policy for materialized views: OnDemand, Scheduled, or Incremental.
    /// </summary>
    public string? RefreshPolicy { get; init; }

    /// <summary>
    /// Cron expression for scheduled refresh.
    /// </summary>
    public string? RefreshSchedule { get; init; }

    /// <summary>
    /// Optional description or metadata.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Column specification for view definition.
/// </summary>
public sealed record ViewColumnApiSpec
{
    /// <summary>
    /// Source column reference (table.column or just column for base table).
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Computed expression (e.g., "price * quantity").
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// Output column alias (required).
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Data type of the output column.
    /// </summary>
    public string? DataType { get; init; }

    /// <summary>
    /// Aggregation function: Count, Sum, Avg, Min, Max, ArrayAgg, StringAgg, First, Last.
    /// </summary>
    public string? Aggregation { get; init; }
}

/// <summary>
/// Join specification for view definition.
/// </summary>
public sealed record ViewJoinApiSpec
{
    /// <summary>
    /// Table to join.
    /// </summary>
    public required string Table { get; init; }

    /// <summary>
    /// Alias for the joined table.
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// Type of join: Inner, Left, Right, Full, Cross.
    /// </summary>
    public string JoinType { get; init; } = "Left";

    /// <summary>
    /// Join condition (e.g., "orders.customer_id = customers._id").
    /// </summary>
    public required string Condition { get; init; }
}

/// <summary>
/// Filter specification for view definition.
/// </summary>
public sealed record ViewFilterApiSpec
{
    /// <summary>
    /// Column or expression to filter on.
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// Comparison operator: eq, neq, gt, gte, lt, lte, like, ilike, in, notin, isnull, isnotnull, between, contains, startswith, endswith.
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Logical operator for combining with other filters: And, Or.
    /// </summary>
    public string LogicalOp { get; init; } = "And";
}

/// <summary>
/// Order specification for view definition.
/// </summary>
public sealed record ViewOrderApiSpec
{
    /// <summary>
    /// Column to order by.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Whether to order descending.
    /// </summary>
    public bool Descending { get; init; }

    /// <summary>
    /// Null ordering: First or Last.
    /// </summary>
    public string NullOrdering { get; init; } = "Last";
}

/// <summary>
/// Request to update a view.
/// </summary>
public sealed record UpdateViewApiRequest
{
    /// <summary>
    /// New name for the view.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Updated columns.
    /// </summary>
    public IReadOnlyList<ViewColumnApiSpec>? Columns { get; init; }

    /// <summary>
    /// Updated joins.
    /// </summary>
    public IReadOnlyList<ViewJoinApiSpec>? Joins { get; init; }

    /// <summary>
    /// Updated filters.
    /// </summary>
    public IReadOnlyList<ViewFilterApiSpec>? Filters { get; init; }

    /// <summary>
    /// Updated group by columns.
    /// </summary>
    public IReadOnlyList<string>? GroupBy { get; init; }

    /// <summary>
    /// Updated ordering.
    /// </summary>
    public IReadOnlyList<ViewOrderApiSpec>? OrderBy { get; init; }

    /// <summary>
    /// Updated limit.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Updated distinct setting.
    /// </summary>
    public bool? Distinct { get; init; }

    /// <summary>
    /// Updated refresh policy.
    /// </summary>
    public string? RefreshPolicy { get; init; }

    /// <summary>
    /// Updated refresh schedule.
    /// </summary>
    public string? RefreshSchedule { get; init; }

    /// <summary>
    /// Updated description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// View API response.
/// </summary>
public sealed record ViewApiResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string BaseTable { get; init; }
    public IReadOnlyList<ViewColumnApiResponse> Columns { get; init; } = [];
    public IReadOnlyList<ViewJoinApiSpec>? Joins { get; init; }
    public IReadOnlyList<ViewFilterApiSpec>? Filters { get; init; }
    public IReadOnlyList<string>? GroupBy { get; init; }
    public IReadOnlyList<ViewOrderApiSpec>? OrderBy { get; init; }
    public int? Limit { get; init; }
    public bool Distinct { get; init; }
    public bool IsMaterialized { get; init; }
    public string? RefreshPolicy { get; init; }
    public string? RefreshSchedule { get; init; }
    public DateTimeOffset? LastRefreshedAt { get; init; }
    public bool IsStale { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static ViewApiResponse FromMetadata(ViewMetadata view)
    {
        return new ViewApiResponse
        {
            Id = view.ViewId,
            Name = view.LogicalName,
            BaseTable = view.Definition.BaseTable,
            Columns = view.Columns.Select(c => new ViewColumnApiResponse
            {
                Name = c.LogicalName,
                DataType = c.DataType.ToString(),
                IsComputed = c.IsComputed,
                Expression = c.Expression
            }).ToList(),
            Joins = view.Definition.Joins.Select(j => new ViewJoinApiSpec
            {
                Table = j.Table,
                Alias = j.Alias,
                JoinType = j.JoinType.ToString(),
                Condition = j.Condition
            }).ToList(),
            Filters = view.Definition.Filters.Select(f => new ViewFilterApiSpec
            {
                Field = f.Field,
                Operator = f.Operator.ToString(),
                Value = f.Value,
                LogicalOp = f.LogicalOp.ToString()
            }).ToList(),
            GroupBy = view.Definition.GroupBy.ToList(),
            OrderBy = view.Definition.OrderBy.Select(o => new ViewOrderApiSpec
            {
                Column = o.Column,
                Descending = o.Descending,
                NullOrdering = o.NullOrdering.ToString()
            }).ToList(),
            Limit = view.Definition.Limit,
            Distinct = view.Definition.Distinct,
            IsMaterialized = view.IsMaterialized,
            RefreshPolicy = view.RefreshPolicy.ToString(),
            RefreshSchedule = view.RefreshSchedule,
            LastRefreshedAt = view.LastRefreshedAt,
            IsStale = view.IsStale,
            CreatedAt = view.CreatedAt,
            UpdatedAt = view.UpdatedAt
        };
    }
}

/// <summary>
/// View column API response.
/// </summary>
public sealed record ViewColumnApiResponse
{
    public required string Name { get; init; }
    public required string DataType { get; init; }
    public bool IsComputed { get; init; }
    public string? Expression { get; init; }
}

/// <summary>
/// Query parameters for view data endpoint.
/// </summary>
public sealed record ViewQueryApiParameters
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
    /// Number of rows to skip.
    /// </summary>
    public int? Skip { get; init; }

    /// <summary>
    /// Number of rows to take.
    /// </summary>
    public int? Take { get; init; }
}

/// <summary>
/// View query result API response.
/// </summary>
public sealed record ViewQueryApiResponse
{
    public required IReadOnlyList<IDictionary<string, object?>> Data { get; init; }
    public long TotalCount { get; init; }
    public bool HasMore { get; init; }
}

#endregion
