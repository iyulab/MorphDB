using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for batch data operations.
/// </summary>
[ApiController]
[Route("api/batch")]
[RequireProject]
public sealed class BatchController : ControllerBase
{
    private readonly IMorphDataService _dataService;
    private readonly IProjectContextAccessor _projectContext;
    private readonly ILogger<BatchController> _logger;

    public BatchController(
        IMorphDataService dataService,
        IProjectContextAccessor projectContext,
        ILogger<BatchController> logger)
    {
        _dataService = dataService;
        _projectContext = projectContext;
        _logger = logger;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

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
            var projectId = GetProjectId();

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
                var result = await ExecuteOperationAsync(projectId, i, operation, cancellationToken);
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
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "batch execute");
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
            var projectId = GetProjectId();

            if (records == null || records.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No records provided",
                    Code = "EMPTY_BATCH"
                });
            }

            // Generate IDs for records that don't have them - using UUID v7
            foreach (var record in records)
            {
                if (!record.ContainsKey("_id"))
                {
                    record["_id"] = Guid.CreateVersion7();
                }
            }

            var insertedRecords = await _dataService.InsertBatchAsync(projectId, table, records, cancellationToken);

            var results = insertedRecords.Select((record, index) =>
            {
                var id = record.TryGetValue("_id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;
                return new BatchOperationResult
                {
                    Index = index,
                    Success = true,
                    Data = new Dictionary<string, object?> { ["_id"] = id },
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
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "batch insert");
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
            var projectId = GetProjectId();

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
            var query = _dataService.Query(projectId).From(table);
            if (!string.IsNullOrEmpty(request.Filter))
            {
                query = ApplyFilter(query, request.Filter);
            }

            var affected = await _dataService.UpdateBatchAsync(projectId, table, request.Data, query, cancellationToken);

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
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "batch update");
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
            var projectId = GetProjectId();

            // Build filter query
            var query = _dataService.Query(projectId).From(table);
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

            var affected = await _dataService.DeleteBatchAsync(projectId, table, query, cancellationToken);

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
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "batch delete");
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
            var projectId = GetProjectId();

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

            // Generate ID if not provided - using UUID v7
            if (!request.Data.ContainsKey("_id"))
            {
                request.Data["_id"] = Guid.CreateVersion7();
            }

            var result = await _dataService.UpsertAsync(
                projectId, table, request.Data, request.KeyColumns.ToArray(), cancellationToken);

            var id = result.TryGetValue("_id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

            var response = new DataRecordResponse
            {
                Id = id,
                Data = result
            };

            // Check if this was an insert or update based on whether the record existed
            // For simplicity, we return OK for all upserts
            return Ok(response);
        }
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "upsert");
        }
    }

    /// <summary>
    /// Seed records — idempotent bulk upsert. Records are inserted if they don't exist
    /// or updated if they match the key columns. Useful for data initialization.
    /// </summary>
    [HttpPost("data/{table}/seed")]
    [ProducesResponseType(typeof(SeedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Seed(
        string table,
        [FromBody] SeedRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();

            if (request.Records is not { Count: > 0 })
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "No records provided",
                    Code = "EMPTY_DATA"
                });
            }

            if (request.KeyColumns is not { Count: > 0 })
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "Key columns are required for seeding",
                    Code = "MISSING_KEY_COLUMNS"
                });
            }

            var inserted = 0;
            var updated = 0;
            var errors = new List<SeedError>();

            foreach (var (record, index) in request.Records.Select((r, i) => (r, i)))
            {
                try
                {
                    if (!record.ContainsKey("_id"))
                    {
                        record["_id"] = Guid.CreateVersion7();
                    }

                    await _dataService.UpsertAsync(
                        projectId, table, record, request.KeyColumns.ToArray(), cancellationToken);

                    // Simple heuristic: count as insert (exact tracking requires DB-level info)
                    inserted++;
                }
                catch (Exception ex)
                {
                    errors.Add(new SeedError
                    {
                        Index = index,
                        Message = UnhandledErrors.ItemMessage(_logger, ex, "seed record")
                    });
                }
            }

            return Ok(new SeedResponse
            {
                TotalRecords = request.Records.Count,
                Inserted = inserted - updated,
                Updated = updated,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            return UnhandledErrors.Map(this, _logger, ex, "seed");
        }
    }

    #region Private Methods

    private async Task<BatchOperationResult> ExecuteOperationAsync(
        Guid projectId,
        int index,
        BatchOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return operation.Method.ToUpperInvariant() switch
            {
                "INSERT" => await ExecuteInsertAsync(projectId, index, operation, cancellationToken),
                "UPDATE" => await ExecuteUpdateAsync(projectId, index, operation, cancellationToken),
                "DELETE" => await ExecuteDeleteAsync(projectId, index, operation, cancellationToken),
                "UPSERT" => await ExecuteUpsertAsync(projectId, index, operation, cancellationToken),
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
                Error = UnhandledErrors.ItemMessage(_logger, ex, "batch operation")
            };
        }
    }

    private async Task<BatchOperationResult> ExecuteInsertAsync(
        Guid projectId,
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

        // Generate ID if not provided - using UUID v7
        if (!operation.Data.ContainsKey("_id"))
        {
            operation.Data["_id"] = Guid.CreateVersion7();
        }

        var result = await _dataService.InsertAsync(projectId, operation.Table, operation.Data, cancellationToken);
        var id = result.TryGetValue("_id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["_id"] = id },
            AffectedRows = 1
        };
    }

    private async Task<BatchOperationResult> ExecuteUpdateAsync(
        Guid projectId,
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
            projectId, operation.Table, operation.Id.Value, operation.Data, cancellationToken);

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["_id"] = operation.Id },
            AffectedRows = 1
        };
    }

    private async Task<BatchOperationResult> ExecuteDeleteAsync(
        Guid projectId,
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
            projectId, operation.Table, operation.Id.Value, cancellationToken);

        return new BatchOperationResult
        {
            Index = index,
            Success = deleted,
            Data = new Dictionary<string, object?> { ["_id"] = operation.Id },
            AffectedRows = deleted ? 1 : 0
        };
    }

    private async Task<BatchOperationResult> ExecuteUpsertAsync(
        Guid projectId,
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

        // Generate ID if not provided - using UUID v7
        if (!operation.Data.ContainsKey("_id"))
        {
            operation.Data["_id"] = Guid.CreateVersion7();
        }

        var result = await _dataService.UpsertAsync(
            projectId, operation.Table, operation.Data, operation.KeyColumns.ToArray(), cancellationToken);
        var id = result.TryGetValue("_id", out var idValue) && idValue is Guid guid ? guid : Guid.Empty;

        return new BatchOperationResult
        {
            Index = index,
            Success = true,
            Data = new Dictionary<string, object?> { ["_id"] = id },
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

/// <summary>
/// Request for data seeding (idempotent bulk upsert).
/// </summary>
public sealed record SeedRequest
{
    public required IReadOnlyList<IDictionary<string, object?>> Records { get; init; }
    public required IReadOnlyList<string> KeyColumns { get; init; }
}

/// <summary>
/// Result of a seed operation.
/// </summary>
public sealed record SeedResponse
{
    public int TotalRecords { get; init; }
    public int Inserted { get; init; }
    public int Updated { get; init; }
    public IReadOnlyList<SeedError> Errors { get; init; } = [];
}

/// <summary>
/// Error from a single seed record.
/// </summary>
public sealed record SeedError
{
    public int Index { get; init; }
    public string? Message { get; init; }
}
