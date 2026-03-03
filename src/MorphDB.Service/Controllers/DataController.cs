using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Models.Api;

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
public sealed class DataController : ControllerBase
{
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline _writePipeline;
    private readonly IMetadataRepository _metadataRepository;
    private readonly ILogger<DataController> _logger;

    public DataController(
        IMorphDataService dataService,
        IWritePipeline writePipeline,
        IMetadataRepository metadataRepository,
        ILogger<DataController> logger)
    {
        _dataService = dataService;
        _writePipeline = writePipeline;
        _metadataRepository = metadataRepository;
        _logger = logger;
    }

    private Guid GetTenantId()
    {
        if (Request.Headers.TryGetValue("X-Tenant-Id", out var tenantIdHeader) &&
            Guid.TryParse(tenantIdHeader.FirstOrDefault(), out var tenantId))
        {
            return tenantId;
        }

        throw new InvalidOperationException("X-Tenant-Id header is required");
    }

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
            var tenantId = GetTenantId();

            // Validate pagination
            var pageSize = Math.Clamp(query.PageSize, 1, Math.Min(1000, MaxPageSize));
            var page = Math.Max(query.Page, 1);

            // Build query
            var morphQuery = _dataService.Query(tenantId).From(table);

            // Select columns
            if (!string.IsNullOrEmpty(query.Select))
            {
                var columns = query.Select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                morphQuery = morphQuery.SelectColumns(columns);
            }
            else
            {
                morphQuery = morphQuery.SelectAll();
            }

            // Apply filters
            if (!string.IsNullOrEmpty(query.Filter))
            {
                morphQuery = ApplyFilters(morphQuery, query.Filter);
            }

            // Apply search across text columns
            if (!string.IsNullOrEmpty(query.Search))
            {
                var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                    tenantId, table, includeColumns: true, cancellationToken);

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
                morphQuery = ApplyOrdering(morphQuery, query.OrderBy);
            }

            // Get total count for pagination
            var totalCount = await morphQuery.CountAsync(cancellationToken);

            // Apply pagination
            morphQuery = morphQuery.Limit(pageSize).Offset((page - 1) * pageSize);

            // Execute query
            var results = await morphQuery.ToListAsync(cancellationToken);

            var records = results.Select(r => new DataRecordResponse
            {
                Id = r.TryGetValue("_id", out var id) && id is Guid guid ? guid : Guid.Empty,
                Data = r
            }).ToList();

            var response = new PagedResponse<DataRecordResponse>
            {
                Data = records,
                Pagination = new PaginationInfo
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                }
            };

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "INVALID_FILTER" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message, Code = "TABLE_NOT_FOUND" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.QueryError(_logger, ex, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
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
        try
        {
            var tenantId = GetTenantId();

            var record = await _dataService.GetByIdAsync(tenantId, table, id, cancellationToken);

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
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message, Code = "TABLE_NOT_FOUND" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.GetByIdError(_logger, ex, id, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
        }
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
            var tenantId = GetTenantId();
            var pageSize = Math.Clamp(request.PageSize, 1, Math.Min(1000, MaxPageSize));
            var page = Math.Max(request.Page, 1);

            var morphQuery = _dataService.Query(tenantId).From(table);

            // Select
            if (request.Select is { Count: > 0 })
            {
                morphQuery = morphQuery.SelectColumns([.. request.Select]);
            }
            else
            {
                morphQuery = morphQuery.SelectAll();
            }

            // Apply complex filter
            if (request.Filter is not null)
            {
                morphQuery = ApplyFilterNode(morphQuery, request.Filter, isFirst: true);
            }

            // Ordering
            if (request.OrderBy is { Count: > 0 })
            {
                morphQuery = ApplyOrdering(morphQuery, string.Join(",", request.OrderBy));
            }

            var totalCount = await morphQuery.CountAsync(cancellationToken);
            morphQuery = morphQuery.Limit(pageSize).Offset((page - 1) * pageSize);

            var results = await morphQuery.ToListAsync(cancellationToken);
            var records = results.Select(r => new DataRecordResponse
            {
                Id = r.TryGetValue("_id", out var id) && id is Guid guid ? guid : Guid.Empty,
                Data = r
            }).ToList();

            return Ok(new PagedResponse<DataRecordResponse>
            {
                Data = records,
                Pagination = new PaginationInfo
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "INVALID_FILTER" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Error = "NotFound", Message = ex.Message, Code = "TABLE_NOT_FOUND" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.QueryError(_logger, ex, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                tenantId, table, includeColumns: true, cancellationToken);

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

            var writeResult = await _writePipeline.InsertAsync(
                tenantId, tableMetadata, data, options, cancellationToken);

            if (!writeResult.Success)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                    Code = "VALIDATION_FAILED"
                });
            }

            var result = writeResult.Data ?? data;
            var id = result.TryGetValue("_id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

            var response = new DataRecordResponse
            {
                Id = id,
                Data = result
            };

            return CreatedAtAction(nameof(GetById), new { table, id }, response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.InsertError(_logger, ex, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
        }
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                tenantId, table, includeColumns: true, cancellationToken);

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
            var existing = await _dataService.GetByIdAsync(tenantId, table, id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "NotFound",
                    Message = $"Record with ID '{id}' not found in table '{table}'",
                    Code = "RECORD_NOT_FOUND"
                });
            }

            var writeResult = await _writePipeline.UpdateAsync(
                tenantId, tableMetadata, id, data, existing, cancellationToken: cancellationToken);

            if (!writeResult.Success)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                    Code = "VALIDATION_FAILED"
                });
            }

            return Ok(new DataRecordResponse
            {
                Id = id,
                Data = writeResult.Data ?? data
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.UpdateError(_logger, ex, id, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
        }
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
        try
        {
            var tenantId = GetTenantId();

            var tableMetadata = await _metadataRepository.GetTableByNameAsync(
                tenantId, table, includeColumns: true, cancellationToken);

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
                tenantId, tableMetadata, id, cancellationToken: cancellationToken);

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
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (Exception ex)
        {
            DataControllerLogs.DeleteError(_logger, ex, id, table);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Error = "InternalError", Message = "An unexpected error occurred", Code = "INTERNAL_ERROR" });
        }
    }

    #endregion

    #region Helper Methods

    private static IMorphQuery ApplyFilterNode(IMorphQuery query, FilterNode node, bool isFirst)
    {
        return node switch
        {
            QueryFilterCondition condition => ApplyCondition(query, condition, isFirst),
            QueryFilterGroup group => ApplyGroup(query, group),
            _ => query
        };
    }

    private static IMorphQuery ApplyCondition(IMorphQuery query, QueryFilterCondition condition, bool isFirst)
    {
        var op = ApiModelExtensions.ParseFilterOperator(condition.Operator);
        var value = condition.Value is System.Text.Json.JsonElement je
            ? ParseJsonFilterValue(je)
            : condition.Value;

        return isFirst
            ? query.Where(condition.Column, op, value)
            : query.AndWhere(condition.Column, op, value);
    }

    private static IMorphQuery ApplyGroup(IMorphQuery query, QueryFilterGroup group)
    {
        if (group.Filters.Count == 0)
            return query;

        var isOr = group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase);
        var isFirst = true;

        foreach (var child in group.Filters)
        {
            if (child is QueryFilterCondition condition)
            {
                var op = ApiModelExtensions.ParseFilterOperator(condition.Operator);
                var value = condition.Value is System.Text.Json.JsonElement je
                    ? ParseJsonFilterValue(je)
                    : condition.Value;

                if (isFirst)
                {
                    query = query.Where(condition.Column, op, value);
                    isFirst = false;
                }
                else if (isOr)
                {
                    query = query.OrWhere(condition.Column, op, value);
                }
                else
                {
                    query = query.AndWhere(condition.Column, op, value);
                }
            }
            else if (child is QueryFilterGroup nestedGroup)
            {
                // Recursively apply nested groups
                query = ApplyGroup(query, nestedGroup);
            }
        }

        return query;
    }

    private static object? ParseJsonFilterValue(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static IMorphQuery ApplyFilters(IMorphQuery query, string filterExpression)
    {
        // Parse filter expression: column:operator:value,column2:operator2:value2
        var filters = filterExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isFirst = true;

        foreach (var filter in filters)
        {
            var parts = filter.Split(':', 3);
            if (parts.Length != 3)
            {
                throw new ArgumentException(
                    $"Invalid filter format: '{filter}'. Expected format: 'column:operator:value'. " +
                    $"For OData syntax, use the /odata endpoint with $filter parameter instead.");
            }

            var column = parts[0].Trim();
            var op = ApiModelExtensions.ParseFilterOperator(parts[1].Trim());
            var value = ParseFilterValue(parts[2].Trim());

            if (isFirst)
            {
                query = query.Where(column, op, value);
                isFirst = false;
            }
            else
            {
                query = query.AndWhere(column, op, value);
            }
        }

        return query;
    }

    private static IMorphQuery ApplyOrdering(IMorphQuery query, string orderExpression)
    {
        // Parse order expression: column:asc,column2:desc
        var orders = orderExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var order in orders)
        {
            var parts = order.Split(':', 2);
            var column = parts[0].Trim();
            var direction = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "asc";

            if (direction == "desc")
            {
                query = query.OrderByDesc(column);
            }
            else
            {
                query = query.OrderBy(column);
            }
        }

        return query;
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

            // Unknown state values are ignored (no filter applied)
            _ => query
        };
    }

    private static object ParseFilterValue(string value)
    {
        // Try to parse as various types
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (long.TryParse(value, out var longValue))
            return longValue;

        if (decimal.TryParse(value, out var decimalValue))
            return decimalValue;

        if (Guid.TryParse(value, out var guidValue))
            return guidValue;

        if (DateTime.TryParse(value, out var dateValue))
            return dateValue;

        // Remove quotes if present
        if (value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];

        if (value.StartsWith('\'') && value.EndsWith('\''))
            return value[1..^1];

        return value;
    }

    #endregion
}
