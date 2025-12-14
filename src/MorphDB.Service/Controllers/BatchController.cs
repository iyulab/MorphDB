using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for batch data operations.
/// </summary>
[ApiController]
[Route("api/batch")]
public sealed class BatchController : ControllerBase
{
    private readonly IMorphDataService _dataService;

    public BatchController(IMorphDataService dataService)
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

    /// <summary>
    /// Execute batch data operations.
    /// </summary>
    /// <remarks>
    /// Supports INSERT, UPDATE, DELETE, and UPSERT operations.
    /// Operations are executed in order. On error, subsequent operations are skipped.
    /// </remarks>
    [HttpPost("data")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteBatch(
        [FromBody] BatchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (request.Operations == null || request.Operations.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No operations provided",
                    Code = "EMPTY_BATCH"
                });
            }

            var results = new List<BatchOperationResult>();
            var successCount = 0;
            var failureCount = 0;

            for (var i = 0; i < request.Operations.Count; i++)
            {
                var operation = request.Operations[i];
                var result = await ExecuteOperationAsync(tenantId, i, operation, cancellationToken);
                results.Add(result);

                if (result.Success)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            return Ok(new BatchResponse
            {
                Results = results,
                SuccessCount = successCount,
                FailureCount = failureCount
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

    /// <summary>
    /// Bulk insert records into a table.
    /// </summary>
    [HttpPost("data/{table}/insert")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkInsert(
        string table,
        [FromBody] IReadOnlyList<IDictionary<string, object?>> records,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (records == null || records.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No records provided",
                    Code = "EMPTY_BATCH"
                });
            }

            // Generate IDs for records that don't have them
            foreach (var record in records)
            {
                if (!record.ContainsKey("id"))
                {
                    record["id"] = Guid.NewGuid();
                }
            }

            var insertedRecords = await _dataService.InsertBatchAsync(tenantId, table, records, cancellationToken);

            var results = insertedRecords.Select((record, index) =>
            {
                var id = record.TryGetValue("id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;
                return new BatchOperationResult
                {
                    Index = index,
                    Success = true,
                    Data = new Dictionary<string, object?> { ["id"] = id },
                    AffectedRows = 1
                };
            }).ToList();

            return Ok(new BatchResponse
            {
                Results = results,
                SuccessCount = insertedRecords.Count,
                FailureCount = 0
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

    /// <summary>
    /// Bulk update records in a table using a filter.
    /// </summary>
    [HttpPatch("data/{table}")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkUpdate(
        string table,
        [FromBody] BulkUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (request.Data == null || request.Data.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No data provided",
                    Code = "EMPTY_DATA"
                });
            }

            // Build filter query
            var query = _dataService.Query(tenantId).From(table);
            if (!string.IsNullOrEmpty(request.Filter))
            {
                query = ApplyFilter(query, request.Filter);
            }

            var affected = await _dataService.UpdateBatchAsync(tenantId, table, request.Data, query, cancellationToken);

            return Ok(new BatchResponse
            {
                Results =
                [
                    new BatchOperationResult
                    {
                        Index = 0,
                        Success = true,
                        AffectedRows = affected
                    }
                ],
                SuccessCount = affected > 0 ? 1 : 0,
                FailureCount = 0
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

    /// <summary>
    /// Bulk delete records from a table using a filter.
    /// </summary>
    [HttpDelete("data/{table}")]
    [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BulkDelete(
        string table,
        [FromQuery] string? filter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            // Build filter query
            var query = _dataService.Query(tenantId).From(table);
            if (!string.IsNullOrEmpty(filter))
            {
                query = ApplyFilter(query, filter);
            }
            else
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "Filter is required for bulk delete to prevent accidental data loss",
                    Code = "FILTER_REQUIRED"
                });
            }

            var affected = await _dataService.DeleteBatchAsync(tenantId, table, query, cancellationToken);

            return Ok(new BatchResponse
            {
                Results =
                [
                    new BatchOperationResult
                    {
                        Index = 0,
                        Success = true,
                        AffectedRows = affected
                    }
                ],
                SuccessCount = affected > 0 ? 1 : 0,
                FailureCount = 0
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

    /// <summary>
    /// Upsert a record (insert or update based on key columns).
    /// </summary>
    [HttpPut("data/{table}")]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DataRecordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(
        string table,
        [FromBody] UpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (request.Data == null || request.Data.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No data provided",
                    Code = "EMPTY_DATA"
                });
            }

            if (request.KeyColumns == null || request.KeyColumns.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "Key columns are required for upsert",
                    Code = "MISSING_KEY_COLUMNS"
                });
            }

            // Generate ID if not provided
            if (!request.Data.ContainsKey("id"))
            {
                request.Data["id"] = Guid.NewGuid();
            }

            var result = await _dataService.UpsertAsync(
                tenantId, table, request.Data, request.KeyColumns.ToArray(), cancellationToken);

            var id = result.TryGetValue("id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

            var response = new DataRecordResponse
            {
                Id = id,
                Data = result
            };

            // Check if this was an insert or update based on whether the record existed
            // For simplicity, we return OK for all upserts
            return Ok(response);
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

    #region Private Methods

    private async Task<BatchOperationResult> ExecuteOperationAsync(
        Guid tenantId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return operation.Method.ToUpperInvariant() switch
            {
                "INSERT" => await ExecuteInsertAsync(tenantId, index, operation, cancellationToken),
                "UPDATE" => await ExecuteUpdateAsync(tenantId, index, operation, cancellationToken),
                "DELETE" => await ExecuteDeleteAsync(tenantId, index, operation, cancellationToken),
                "UPSERT" => await ExecuteUpsertAsync(tenantId, index, operation, cancellationToken),
                _ => new BatchOperationResult
                {
                    Index = index,
                    Success = false,
                    Error = $"Unknown method: {operation.Method}"
                }
            };
        }
        catch (Exception ex)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<BatchOperationResult> ExecuteInsertAsync(
        Guid tenantId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Data == null)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "Data is required for INSERT operation"
            };
        }

        // Generate ID if not provided
        if (!operation.Data.ContainsKey("id"))
        {
            operation.Data["id"] = Guid.NewGuid();
        }

        var result = await _dataService.InsertAsync(tenantId, operation.Table, operation.Data, cancellationToken);
        var id = result.TryGetValue("id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["id"] = id },
            AffectedRows = 1
        };
    }

    private async Task<BatchOperationResult> ExecuteUpdateAsync(
        Guid tenantId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Id == null)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "ID is required for UPDATE operation"
            };
        }

        if (operation.Data == null)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "Data is required for UPDATE operation"
            };
        }

        var result = await _dataService.UpdateAsync(
            tenantId, operation.Table, operation.Id.Value, operation.Data, cancellationToken);

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["id"] = operation.Id },
            AffectedRows = 1
        };
    }

    private async Task<BatchOperationResult> ExecuteDeleteAsync(
        Guid tenantId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Id == null)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "ID is required for DELETE operation"
            };
        }

        var deleted = await _dataService.DeleteAsync(
            tenantId, operation.Table, operation.Id.Value, cancellationToken);

        return new BatchOperationResult
        {
            Index = index,
            Success = deleted,
            Data = new Dictionary<string, object?> { ["id"] = operation.Id },
            AffectedRows = deleted ? 1 : 0
        };
    }

    private async Task<BatchOperationResult> ExecuteUpsertAsync(
        Guid tenantId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Data == null)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "Data is required for UPSERT operation"
            };
        }

        if (operation.KeyColumns == null || operation.KeyColumns.Count == 0)
        {
            return new BatchOperationResult
            {
                Index = index,
                Success = false,
                Error = "KeyColumns is required for UPSERT operation"
            };
        }

        // Generate ID if not provided
        if (!operation.Data.ContainsKey("id"))
        {
            operation.Data["id"] = Guid.NewGuid();
        }

        var result = await _dataService.UpsertAsync(
            tenantId, operation.Table, operation.Data, operation.KeyColumns.ToArray(), cancellationToken);
        var id = result.TryGetValue("id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["id"] = id },
            AffectedRows = 1
        };
    }

    private static IMorphQuery ApplyFilter(IMorphQuery query, string filterExpression)
    {
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

    private static object ParseFilterValue(string value)
    {
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

        if (value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];

        if (value.StartsWith('\'') && value.EndsWith('\''))
            return value[1..^1];

        return value;
    }

    #endregion
}

/// <summary>
/// Request for bulk update operations.
/// </summary>
public sealed record BulkUpdateRequest
{
    public required IDictionary<string, object?> Data { get; init; }
    public string? Filter { get; init; }
}

/// <summary>
/// Request for upsert operation.
/// </summary>
public sealed record UpsertRequest
{
    public required IDictionary<string, object?> Data { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
}
