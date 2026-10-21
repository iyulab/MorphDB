using System.Text.Json;
using HotChocolate;
using HotChocolate.Types;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;
using AggregationColumn = MorphDB.Core.Abstractions.AggregationColumn;
using AggregationOrderBy = MorphDB.Core.Abstractions.AggregationOrderBy;
using AggregationRequest = MorphDB.Core.Abstractions.AggregationRequest;
using FilterCondition = MorphDB.Core.Abstractions.FilterCondition;
using HavingCondition = MorphDB.Core.Abstractions.HavingCondition;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// The read half of the GraphQL surface: schema reads (which tables exist, what one looks like)
/// and data reads (query, get by id, aggregate).
/// <para>
/// Every operation here names its table in an argument rather than in a type, so this class does
/// not change when a table is created — see <see cref="MorphDbSchemaBuilder"/> for why the schema
/// is fixed even though what it serves is not.
/// </para>
/// </summary>
[ExtendObjectType(typeof(Query))]
public sealed class MorphDbQuery
{
    /// <summary>
    /// Lists all tables for the current project.
    /// </summary>
    [GraphQLDescription("Lists all tables for the current project")]
    public async Task<IReadOnlyList<TableGraphType>> GetTables(
        [Service] ISchemaManager schemaManager,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        var projectId = projectAccessor.ProjectId;
        var tables = await schemaManager.ListTablesAsync(projectId, cancellationToken);

        return tables.Select(t => new TableGraphType
        {
            Name = t.LogicalName,
            Version = t.SchemaVersion,
            Columns = t.ExposedColumns().Select(c => new ColumnGraphType
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
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        var projectId = projectAccessor.ProjectId;
        var table = await schemaManager.GetTableAsync(projectId, name, cancellationToken);

        if (table is null)
            return null;

        return new TableGraphType
        {
            Name = table.LogicalName,
            Version = table.SchemaVersion,
            Columns = table.ExposedColumns().Select(c => new ColumnGraphType
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
        IReadOnlyList<string>? filter,
        string? orderBy,
        [Service] IMorphDataService dataService,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        var projectId = projectAccessor.ProjectId;

        // The REST list's languages, read by the same code: one condition per filter entry, and a
        // malformed condition or ordering is refused rather than dropped. A builder is consumed by what
        // it runs, so the rows and the count each get their own.
        IMorphQuery Build()
        {
            var query = FilterExpressions.Apply(dataService.Query(projectId).From(table).SelectAll(), filter);
            return string.IsNullOrWhiteSpace(orderBy)
                ? query.OrderByDesc(SystemColumns.CreatedAt).OrderByDesc(SystemColumns.Id)
                : OrderExpressions.Apply(query, orderBy);
        }

        IMorphQuery rows;
        try
        {
            rows = Build();
        }
        catch (ArgumentException ex)
        {
            throw new GraphQLException(ErrorBuilder.New()
                .SetMessage(ex.Message)
                .SetCode("INVALID_FILTER")
                .Build());
        }

        // A cursor is a row's position in this order. Paging by position follows whatever order was
        // asked for (the query ends it with _id, so it is total); a cursor holding the row's id was
        // compared against _id whatever the order, and read the newest-first list's next page as the
        // rows newer than the last one — the first page again. The page is read one row past its size,
        // so whether more follow is known without counting.
        var pageSize = Math.Clamp(first ?? 20, 1, 100);
        var skip = string.IsNullOrEmpty(after) ? 0 : DecodeCursor(after) + 1;
        var (records, more) = PaginationInfo.Probe(await rows.Limit(pageSize + 1).Offset(skip).ToListAsync(cancellationToken), pageSize);

        var edges = records.Select((r, i) => new RecordEdge
        {
            Node = CreateRecordNode(r),
            Cursor = EncodeCursor(skip + i)
        }).ToList();

        // Counted only when a selection reads totalCount — on the connection, its pageInfo, or both (once).
        var count = new Lazy<Task<int>>(async () => (int)await Build().CountAsync(cancellationToken));

        return new RecordConnection(count)
        {
            Edges = edges,
            PageInfo = new PageInfo(count)
            {
                HasNextPage = more,
                HasPreviousPage = skip > 0,
                StartCursor = edges.FirstOrDefault()?.Cursor,
                EndCursor = edges.LastOrDefault()?.Cursor,
            },
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
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        var projectId = projectAccessor.ProjectId;
        var record = await dataService.GetByIdAsync(projectId, table, id, cancellationToken);

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

    /// <summary>
    /// Performs aggregation on a table with optional grouping.
    /// Follows Hasura-like _aggregate pattern.
    /// </summary>
    /// <remarks>
    /// Example:
    /// ```graphql
    /// query {
    ///   aggregate(
    ///     table: "orders"
    ///     aggregations: [
    ///       { function: COUNT, alias: "total_orders" }
    ///       { function: SUM, column: "amount", alias: "total_amount" }
    ///     ]
    ///     groupBy: ["status"]
    ///   ) {
    ///     data
    ///     totalGroups
    ///     metadata { rowsScanned executionTimeMs }
    ///   }
    /// }
    /// ```
    /// </remarks>
    [GraphQLDescription("Performs aggregation on a table with optional grouping")]
    public async Task<AggregateResult> Aggregate(
        string table,
        IReadOnlyList<AggregationInput> aggregations,
        IReadOnlyList<string>? groupBy,
        IReadOnlyList<FilterInput>? filter,
        IReadOnlyList<HavingInput>? having,
        IReadOnlyList<OrderByInput>? orderBy,
        int? limit,
        int? offset,
        [Service] IAggregationService aggregationService,
        [Service] IProjectContextAccessor projectAccessor,
        CancellationToken cancellationToken)
    {
        var projectId = projectAccessor.ProjectId;

        var request = new AggregationRequest
        {
            Aggregations = aggregations.Select(a => new AggregationColumn
            {
                Function = a.Function,
                Column = a.Column,
                Alias = a.Alias,
                Distinct = a.Distinct ?? false,
                Limit = a.Limit,
                OrderBy = a.OrderBy
            }).ToList(),
            GroupBy = groupBy ?? [],
            Filter = filter?.Select(f => new FilterCondition
            {
                Column = f.Column,
                Operator = ApiModelExtensions.ParseFilterOperator(f.Operator),
                Value = GraphQlAny.ToRuntimeValue(f.Value)
            }).ToList(),
            Having = having?.Select(h => new HavingCondition
            {
                Alias = h.Alias,
                Operator = ApiModelExtensions.ParseFilterOperator(h.Operator),
                Value = GraphQlAny.ToRuntimeValue(h.Value) ?? 0
            }).ToList(),
            OrderBy = orderBy?.Select(o => new AggregationOrderBy
            {
                Column = o.Column,
                Descending = OrderExpressions.IsDescending(o.Direction, o.Column)
            }).ToList(),
            Limit = limit,
            Offset = offset
        };

        var result = await aggregationService.AggregateAsync(projectId, table, request, cancellationToken);

        return new AggregateResult
        {
            Data = GraphQlAny.FromRows(result.Data),
            TotalGroups = result.TotalGroups,
            Metadata = result.Metadata != null
                ? new AggregateMetadata
                {
                    RowsScanned = result.Metadata.RowsScanned,
                    ExecutionTimeMs = result.Metadata.ExecutionTimeMs
                }
                : null
        };
    }

    private const string CursorPrefix = "offset:";

    private static string EncodeCursor(long position) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CursorPrefix + position.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static int DecodeCursor(string cursor)
    {
        // A cursor this server did not issue is the caller's error. Decoding it to the first page, as
        // this once did, answered a malformed request with a well-formed one and no signal.
        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            if (text.StartsWith(CursorPrefix, StringComparison.Ordinal)
                && int.TryParse(text.AsSpan(CursorPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var position))
            {
                return position;
            }
        }
        catch (FormatException)
        {
        }

        throw new GraphQLException(ErrorBuilder.New()
            .SetMessage($"'{cursor}' is not a cursor this server issued; pass a cursor from a previous page's edges.")
            .SetCode("INVALID_CURSOR")
            .Build());
    }

    private static RecordNode CreateRecordNode(IDictionary<string, object?> r)
    {
        return new RecordNode
        {
            Id = SystemColumns.RequireRecordId(r),
            Data = GraphQlAny.FromRow(r),
            CreatedAt = r.TryGetValue("_created_at", out var createdAt) && createdAt is DateTimeOffset ca ? ca : null,
            UpdatedAt = r.TryGetValue("_updated_at", out var updatedAt) && updatedAt is DateTimeOffset ua ? ua : null
        };
    }
}

#region GraphQL Types

/// <summary>
/// GraphQL representation of a table.
/// Note: PhysicalName is intentionally NOT exposed per MorphDB philosophy
/// ("물리적 이름 외부 노출 금지" - Don't expose physical names externally).
/// </summary>
public sealed class TableGraphType
{
    public required string Name { get; init; }
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
public sealed class RecordConnection(Lazy<Task<int>> count)
{
    public IReadOnlyList<RecordEdge> Edges { get; init; } = [];
    public required PageInfo PageInfo { get; init; }

    /// <summary>The rows the query matches, counted only when this field is selected.</summary>
    public Task<int> TotalCount => count.Value;
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
    public JsonElement Data { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>
/// Page information for pagination.
/// </summary>
public sealed class PageInfo(Lazy<Task<int>> count)
{
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
    public string? StartCursor { get; init; }
    public string? EndCursor { get; init; }

    /// <summary>The rows the query matches, counted only when this field is selected.</summary>
    public Task<int> TotalCount => count.Value;
}

#endregion

#region Aggregation GraphQL Types

/// <summary>
/// Input type for aggregation function specification.
/// </summary>
public sealed class AggregationInput
{
    /// <summary>
    /// Aggregation function to apply.
    /// </summary>
    public required AggregateFunction Function { get; init; }

    /// <summary>
    /// Column to aggregate (null for COUNT(*)).
    /// </summary>
    public string? Column { get; init; }

    /// <summary>
    /// Alias for the result.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Whether to use DISTINCT.
    /// </summary>
    public bool? Distinct { get; init; }

    /// <summary>
    /// For ARRAY_AGG only: at most this many values per group, the first in the array's order.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// For ARRAY_AGG only: the order the array follows, as comma-separated <c>column [asc|desc]</c> terms; the collected column, ascending, by default.
    /// </summary>
    public string? OrderBy { get; init; }
}

/// <summary>
/// Input type for filter conditions.
/// </summary>
public sealed class FilterInput
{
    /// <summary>
    /// Column to filter on.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Filter operator (eq, neq, gt, gte, lt, lte, like, etc.).
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    [GraphQLType(typeof(AnyType))]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Input type for HAVING conditions.
/// </summary>
public sealed class HavingInput
{
    /// <summary>
    /// Aggregation alias to filter on.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Comparison operator (eq, neq, gt, gte, lt, lte).
    /// </summary>
    public required string Operator { get; init; }

    /// <summary>
    /// Value to compare against.
    /// </summary>
    [GraphQLType(typeof(AnyType))]
    public JsonElement Value { get; init; }
}

/// <summary>
/// Input type for ORDER BY specification.
/// </summary>
public sealed class OrderByInput
{
    /// <summary>
    /// Column or alias to order by.
    /// </summary>
    public required string Column { get; init; }

    /// <summary>
    /// Sort direction (asc or desc).
    /// </summary>
    public string? Direction { get; init; }
}

/// <summary>
/// Result of an aggregation query.
/// </summary>
public sealed class AggregateResult
{
    /// <summary>
    /// Aggregated data rows.
    /// </summary>
    [GraphQLType(typeof(ListType<AnyType>))]
    public IReadOnlyList<JsonElement> Data { get; init; } = [];

    /// <summary>
    /// Total number of groups (before limit/offset).
    /// </summary>
    public long? TotalGroups { get; init; }

    /// <summary>
    /// Execution metadata.
    /// </summary>
    public AggregateMetadata? Metadata { get; init; }
}

/// <summary>
/// Metadata about the aggregation execution.
/// </summary>
public sealed class AggregateMetadata
{
    /// <summary>
    /// Number of rows scanned.
    /// </summary>
    public long RowsScanned { get; init; }

    /// <summary>
    /// Execution time in milliseconds.
    /// </summary>
    public double ExecutionTimeMs { get; init; }
}

#endregion
