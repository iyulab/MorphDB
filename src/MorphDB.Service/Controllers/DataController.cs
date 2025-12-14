using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
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

    public DataController(IMorphDataService dataService)
    {
        _dataService = dataService;
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
                Id = r.TryGetValue("id", out var id) && id is Guid guid ? guid : Guid.Empty,
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
    [HttpPost("{table}")]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Insert(
        string table,
        [FromBody] IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            // Generate ID if not provided
            if (!data.ContainsKey("id"))
            {
                data["id"] = Guid.NewGuid();
            }

            var result = await _dataService.InsertAsync(tenantId, table, data, cancellationToken);
            var id = result.TryGetValue("id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

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
            if (parts.Length != 3) continue;

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
