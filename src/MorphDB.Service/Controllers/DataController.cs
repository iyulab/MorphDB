using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

using static MorphDB.Core.Abstractions.QueryLimits;

namespace MorphDB.Service.Controllers;

internal static partial class DataControllerLogs
{
    [LoggerMessage(LogLevel.Error, "Unexpected error querying table '{TableName}'")]
    public static partial void QueryError(ILogger logger, Exception exception, string tableName);

    [LoggerMessage(LogLevel.Error, "Unexpected error getting record '{RecordId}' from table '{TableName}'")]
    public static partial void GetByIdError(ILogger logger, Exception exception, Guid recordId, string tableName);

    [LoggerMessage(LogLevel.Error, "Unexpected error inserting into table '{TableName}'")]
    public static partial void InsertError(ILogger logger, Exception exception, string tableName);

    [LoggerMessage(LogLevel.Error, "Unexpected error updating record '{RecordId}' in table '{TableName}'")]
    public static partial void UpdateError(ILogger logger, Exception exception, Guid recordId, string tableName);

    [LoggerMessage(LogLevel.Error, "Unexpected error deleting record '{RecordId}' from table '{TableName}'")]
    public static partial void DeleteError(ILogger logger, Exception exception, Guid recordId, string tableName);
}

/// <summary>
/// Controller for data CRUD operations.
/// </summary>
[ApiController]
[Route("api/data")]
[RequireProject]
public sealed class DataController : ControllerBase
{
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline _writePipeline;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILogger<DataController> _logger;
    private readonly IProjectContextAccessor _projectContext;

    public DataController(
        IMorphDataService dataService,
        IWritePipeline writePipeline,
        IMetadataRepository metadataRepository,
        ILogger<DataController> logger,
        IProjectContextAccessor projectContext)
    {
        _dataService = dataService;
        _writePipeline = writePipeline;
        _metadataRepository = metadataRepository;
        _logger = logger;
        _projectContext = projectContext;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

    #region Query Operations

    /// <summary>
    /// Query records from a table with optional filtering, sorting, and pagination.
    /// </summary>
    [HttpGet("{table}")]
    [ProducesResponseType(typeof(PagedResponse<DataRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Query(
        string table,
        [FromQuery] DataQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();

            // Validate pagination
            var pageSize = Math.Clamp(query.PageSize, 1, Math.Min(1000, MaxPageSize));
            var page = Math.Max(query.Page, 1);
            var skip = PaginationInfo.Skip(page, pageSize, query.Offset);

            // Build query
            var morphQuery = _dataService.Query(projectId).From(table);
            if (query.IncludeDeleted)
            {
                morphQuery = morphQuery.IncludeDeleted();
            }

            // Select columns
            if (!string.IsNullOrEmpty(query.Select))
            {
                var columns = query.Select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                morphQuery = morphQuery.SelectColumns([.. SystemColumns.WithRecordId(columns)]);
            }
            else
            {
                morphQuery = morphQuery.SelectAll();
            }

            // Apply filters
            morphQuery = FilterExpressions.Apply(morphQuery, query.Filter);

            // Apply search across text columns
            if (!string.IsNullOrEmpty(query.Search))
            {
                var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                    projectId, table, includeColumns: true, cancellationToken);

                if (tableMetadata is not null)
                {
                    morphQuery = ApplySearch(morphQuery, query.Search, tableMetadata.Columns);
                }
            }

            // Apply row state filter if specified
            morphQuery = ApplyRowStateFilter(morphQuery, query.State);

            // Apply ordering
            if (!string.IsNullOrEmpty(query.OrderBy))
            {
                morphQuery = OrderExpressions.Apply(morphQuery, query.OrderBy);
            }

            // Count only when asked; the page is read one row past its size to know whether more follow
            long? totalCount = query.Count ? await morphQuery.CountAsync(cancellationToken) : null;
            var (results, more) = PaginationInfo.Probe(await morphQuery.Limit(pageSize + 1).Offset(skip).ToListAsync(cancellationToken), pageSize);

            var records = results.Select(r => new DataRecordResponse
            {
                Id = SystemColumns.RequireRecordId(r),
                Data = r
            }).ToList();

            var response = new PagedResponse<DataRecordResponse>
            {
                Data = records,
                Pagination = new PaginationInfo
                {
                    Page = query.Offset is null ? page : PaginationInfo.PageOf(skip, pageSize),
                    PageSize = pageSize,
                    Offset = query.Offset,
                    TotalCount = totalCount,
                    HasNext = more
                }
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "INVALID_FILTER" });
        }
    }

    /// <summary>
    /// Get a single record by ID.
    /// </summary>
    [HttpGet("{table}/{id:guid}")]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        string table,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var record = await _dataService.GetByIdAsync(projectId, table, id, cancellationToken);

        if (record == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Record with ID '{id}' not found in table '{table}'",
                Code = "RECORD_NOT_FOUND"
            });
        }

        return Ok(new DataRecordResponse
        {
            Id = id,
            Data = record
        });
    }

    /// <summary>
    /// Complex query with JSON-based AND/OR filter support.
    /// </summary>
    [HttpPost("{table}/query")]
    [ProducesResponseType(typeof(PagedResponse<DataRecordResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ComplexQuery(
        string table,
        [FromBody] ComplexQueryApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var pageSize = Math.Clamp(request.PageSize, 1, Math.Min(1000, MaxPageSize));
            var page = Math.Max(request.Page, 1);
            var skip = PaginationInfo.Skip(page, pageSize, request.Offset);

            var morphQuery = _dataService.Query(projectId).From(table);
            if (request.IncludeDeleted)
            {
                morphQuery = morphQuery.IncludeDeleted();
            }

            // Select
            if (request.Select is { Count: > 0 })
            {
                morphQuery = morphQuery.SelectColumns([.. SystemColumns.WithRecordId(request.Select)]);
            }
            else
            {
                morphQuery = morphQuery.SelectAll();
            }

            // Apply complex filter
            if (request.Filter is not null)
            {
                morphQuery = ApplyFilterNode(morphQuery, request.Filter, or: false);
            }

            // Ordering
            if (request.OrderBy is { Count: > 0 })
            {
                morphQuery = OrderExpressions.Apply(morphQuery, string.Join(",", request.OrderBy));
            }

            long? totalCount = request.Count ? await morphQuery.CountAsync(cancellationToken) : null;
            var (results, more) = PaginationInfo.Probe(await morphQuery.Limit(pageSize + 1).Offset(skip).ToListAsync(cancellationToken), pageSize);
            var records = results.Select(r => new DataRecordResponse
            {
                Id = SystemColumns.RequireRecordId(r),
                Data = r
            }).ToList();

            return Ok(new PagedResponse<DataRecordResponse>
            {
                Data = records,
                Pagination = new PaginationInfo
                {
                    Page = request.Offset is null ? page : PaginationInfo.PageOf(skip, pageSize),
                    PageSize = pageSize,
                    Offset = request.Offset,
                    TotalCount = totalCount,
                    HasNext = more
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "INVALID_FILTER" });
        }
    }

    #endregion

    #region Insert Operations

    /// <summary>
    /// Insert a new record.
    /// </summary>
    /// <param name="table">The table name.</param>
    /// <param name="data">The record data.</param>
    /// <param name="mode">Write mode: "default" or "draft". Draft mode skips validation and sets _row_state='draft'.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{table}")]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Insert(
        string table,
        [FromBody] IDictionary<string, object?> data,
        [FromQuery] string? mode = null,
        [FromQuery] bool ignoreUnknown = false,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var tableMetadata = await _metadataRepository.GetTableByNameAsync(
            projectId, table, includeColumns: true, cancellationToken);

        if (tableMetadata is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Table '{table}' not found",
                Code = "TABLE_NOT_FOUND"
            });
        }

        // Determine write options based on mode
        WriteOptions? options = null;
        if (mode?.Equals("draft", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!tableMetadata.RowStateEnabled)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = $"Table '{table}' does not have row state enabled. Draft mode is not supported.",
                    Code = "ROW_STATE_NOT_ENABLED"
                });
            }
            options = WriteOptions.DraftMode;
        }

        // The explicit opt-in that turns dropping unknown fields from data loss into a feature.
        if (ignoreUnknown)
        {
            options = (options ?? WriteOptions.Default) with { AllowUnknownFields = true };
        }

        var writeResult = await _writePipeline.InsertAsync(
            projectId, tableMetadata, data, options, cancellationToken);

        if (!writeResult.Success)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                Code = ErrorHandling.WriteFailure.CodeFor(writeResult)
            });
        }

        var result = writeResult.Data ?? data;
        var id = SystemColumns.RequireRecordId(result);

        var response = new DataRecordResponse
        {
            Id = id,
            Data = result
        };

        return CreatedAtAction(nameof(GetById), new { table, id }, response);
    }

    #endregion

    #region Update Operations

    /// <summary>
    /// Update an existing record.
    /// </summary>
    [HttpPatch("{table}/{id:guid}")]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string table,
        Guid id,
        [FromBody] IDictionary<string, object?> data,
        [FromQuery] bool ignoreUnknown = false,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var tableMetadata = await _metadataRepository.GetTableByNameAsync(
            projectId, table, includeColumns: true, cancellationToken);

        if (tableMetadata is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Table '{table}' not found",
                Code = "TABLE_NOT_FOUND"
            });
        }

        // Check if record exists first
        var existing = await _dataService.GetByIdAsync(projectId, table, id, cancellationToken);
        if (existing == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Record with ID '{id}' not found in table '{table}'",
                Code = "RECORD_NOT_FOUND"
            });
        }

        var updateOptions = ignoreUnknown
            ? WriteOptions.Default with { AllowUnknownFields = true }
            : null;
        var writeResult = await _writePipeline.UpdateAsync(
            projectId, tableMetadata, id, data, existing, updateOptions, cancellationToken);

        if (ErrorHandling.WriteFailure.IsNotFound(writeResult))
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Record with ID '{id}' not found in table '{table}'",
                Code = "RECORD_NOT_FOUND"
            });
        }

        if (!writeResult.Success)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                Code = ErrorHandling.WriteFailure.CodeFor(writeResult)
            });
        }

        return Ok(new DataRecordResponse
        {
            Id = id,
            Data = writeResult.Data ?? data
        });
    }

    #endregion

    #region Delete Operations

    /// <summary>
    /// Delete a record by ID.
    /// </summary>
    [HttpDelete("{table}/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        string table,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var projectId = GetProjectId();

        var tableMetadata = await _metadataRepository.GetTableByNameAsync(
            projectId, table, includeColumns: true, cancellationToken);

        if (tableMetadata is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Table '{table}' not found",
                Code = "TABLE_NOT_FOUND"
            });
        }

        var writeResult = await _writePipeline.DeleteAsync(
            projectId, tableMetadata, id, cancellationToken: cancellationToken);

        if (!writeResult.Success)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"Record with ID '{id}' not found in table '{table}'",
                Code = "RECORD_NOT_FOUND"
            });
        }

        return NoContent();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// A node of the filter tree onto the query, joined to what precedes it by AND, or by OR when
    /// <paramref name="or"/>: a condition as one condition, a group as one parenthesized condition whose
    /// own nodes are joined by its logic — so <c>a AND (b OR c)</c> stays that, at any depth.
    /// </summary>
    private static IMorphQuery ApplyFilterNode(IMorphQuery query, FilterNode node, bool or) => node switch
    {
        QueryFilterCondition condition => or
            ? query.OrWhere(condition.Column, ApiModelExtensions.ParseFilterOperator(condition.Operator), ValueOf(condition))
            : query.AndWhere(condition.Column, ApiModelExtensions.ParseFilterOperator(condition.Operator), ValueOf(condition)),
        QueryFilterGroup { Filters.Count: 0 } => query,
        QueryFilterGroup group => or
            ? query.OrWhereGroup(inner => ApplyGroupNodes(inner, group))
            : query.WhereGroup(inner => ApplyGroupNodes(inner, group)),
        _ => query,
    };

    private static IMorphQuery ApplyGroupNodes(IMorphQuery query, QueryFilterGroup group)
    {
        var or = group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase);
        for (var i = 0; i < group.Filters.Count; i++)
        {
            query = ApplyFilterNode(query, group.Filters[i], or: or && i > 0);
        }

        return query;
    }

    private static object? ValueOf(QueryFilterCondition condition) =>
        ApiModelExtensions.ParseFilterOperator(condition.Operator) is var op && op.TakesRelatedRows()
            ? RelatedRowsJson.ForOperator(op, condition.Value)
            : condition.Value is System.Text.Json.JsonElement je ? ParseJsonFilterValue(je) : condition.Value;

    private static object? ParseJsonFilterValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Select(FilterExpressions.ListElement).ToArray(),
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Determines if a MorphDataType is a text-like type suitable for ILIKE search.
    /// </summary>
    private static bool IsTextSearchable(MorphDataType dataType) => dataType is
        MorphDataType.Text or
        MorphDataType.LongText or
        MorphDataType.Email or
        MorphDataType.Url or
        MorphDataType.Phone or
        MorphDataType.SingleSelect;

    private static IMorphQuery ApplySearch(
        IMorphQuery query,
        string searchText,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var textColumns = columns
            .Where(c => !c.IsSystemColumn && !c.IsDerived && IsTextSearchable(c.DataType))
            .ToList();

        if (textColumns.Count == 0)
            return query;

        var searchPattern = $"%{searchText}%";
        var isFirst = true;

        foreach (var column in textColumns)
        {
            if (isFirst)
            {
                query = query.Where(column.LogicalName, FilterOperator.ILike, searchPattern);
                isFirst = false;
            }
            else
            {
                query = query.OrWhere(column.LogicalName, FilterOperator.ILike, searchPattern);
            }
        }

        return query;
    }

    private static IMorphQuery ApplyRowStateFilter(IMorphQuery query, string? state)
    {
        // If state is not specified, don't apply any filter (backward compatible).
        // This allows queries on tables without RowStateEnabled to work normally.
        if (string.IsNullOrWhiteSpace(state))
        {
            return query;
        }

        // Normalize state to lowercase
        var normalizedState = state.Trim().ToLowerInvariant();

        return normalizedState switch
        {
            // "all" returns all records regardless of _row_state
            "all" => query,

            // "draft" returns only draft records
            "draft" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Draft.ToString().ToLowerInvariant()),

            // "error" returns only error records
            "error" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Error.ToString().ToLowerInvariant()),

            // "valid" returns valid records only
            "valid" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Valid.ToString().ToLowerInvariant()),

            // An unknown state used to apply no filter at all — an answer wider than the question.
            _ => throw new ArgumentException($"Unknown row state '{state}'. Supported: valid, draft, error, all.")
        };
    }

    #endregion
}
