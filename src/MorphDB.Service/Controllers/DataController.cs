using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for data CRUD operations.
/// </summary>
[ApiController]
[Route("api/data")]
public sealed class DataController : ControllerBase
{
    private readonly IMorphDataService _dataService;
    private readonly IWritePipeline? _writePipeline;
    private readonly IMetadataRepository? _metadataRepository;

    public DataController(
        IMorphDataService dataService,
        IWritePipeline? writePipeline = null,
        IMetadataRepository? metadataRepository = null)
    {
        _dataService = dataService;
        _writePipeline = writePipeline;
        _metadataRepository = metadataRepository;
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
            var pageSize = Math.Clamp(query.PageSize, 1, 1000);
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
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message });
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
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message });
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

            // Generate ID if not provided - IdApplier will auto-generate with UUID v7
            if (!data.ContainsKey("_id"))
            {
                data["_id"] = Guid.CreateVersion7();
            }

            IDictionary<string, object?> result;

            // Use draft mode if requested and pipeline is available
            if (mode?.Equals("draft", StringComparison.OrdinalIgnoreCase) == true
                && _writePipeline is not null
                && _metadataRepository is not null)
            {
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

                if (!tableMetadata.RowStateEnabled)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Error = "BadRequest",
                        Message = $"Table '{table}' does not have row state enabled. Draft mode is not supported.",
                        Code = "ROW_STATE_NOT_ENABLED"
                    });
                }

                var writeResult = await _writePipeline.InsertAsync(
                    tenantId, tableMetadata, data, WriteOptions.DraftMode, cancellationToken);

                if (!writeResult.Success)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Error = "ValidationError",
                        Message = string.Join("; ", writeResult.Errors.Select(e => e.Message)),
                        Code = "VALIDATION_FAILED"
                    });
                }

                result = writeResult.Data ?? data;
            }
            else
            {
                result = await _dataService.InsertAsync(tenantId, table, data, cancellationToken);
            }

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
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message });
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

            var result = await _dataService.UpdateAsync(tenantId, table, id, data, cancellationToken);

            return Ok(new DataRecordResponse
            {
                Id = id,
                Data = result
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Tenant-Id"))
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message, Code = "MISSING_TENANT" });
        }
        catch (Exception ex)
        {
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message });
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

            var deleted = await _dataService.DeleteAsync(tenantId, table, id, cancellationToken);

            if (!deleted)
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
            return BadRequest(new ErrorResponse { Error = "BadRequest", Message = ex.Message });
        }
    }

    #endregion

    #region Helper Methods

    private static IMorphQuery ApplyFilters(IMorphQuery query, string filterExpression)
    {
        // Parse filter expression: column:operator:value,column2:operator2:value2
        var filters = filterExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isFirst = true;

        foreach (var filter in filters)
        {
            var parts = filter.Split(':', 3);
            if (parts.Length != 3)
                continue;

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
