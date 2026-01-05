using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for cross-entity transactions and row-state operations.
/// </summary>
[ApiController]
[Route("api")]
public sealed class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;

    public TransactionController(ITransactionService transactionService)
    {
        _transactionService = transactionService;
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
    /// Execute a cross-entity transaction with $ref support.
    /// </summary>
    /// <remarks>
    /// Operations execute atomically in order. On failure, all operations are rolled back.
    /// Use $ref syntax to reference previous operation results:
    /// - "$order._id" references the _id of an operation with ref "order"
    /// - "$item.quantity" references the quantity field of operation with ref "item"
    /// </remarks>
    [HttpPost("batch/transaction")]
    [ProducesResponseType(typeof(TransactionApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteTransaction(
        [FromBody] TransactionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (request.Operations.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "Transaction must contain at least one operation",
                    Code = "EMPTY_TRANSACTION"
                });
            }

            var coreRequest = MapToTransactionRequest(request);
            var result = await _transactionService.ExecuteAsync(tenantId, coreRequest, cancellationToken);

            return Ok(MapToTransactionResponse(result));
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
    /// Finalize (validate) a single record that was saved in draft mode.
    /// </summary>
    /// <remarks>
    /// Changes _row_state from 'draft' to 'valid' (if validation passes)
    /// or 'error' (if validation fails with errors stored in _row_errors).
    /// </remarks>
    [HttpPatch("data/{table}/{id}/finalize")]
    [ProducesResponseType(typeof(FinalizeResultApi), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FinalizeRecord(
        string table,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            var result = await _transactionService.FinalizeAsync(tenantId, table, id, cancellationToken);

            if (!result.Success && result.Errors.Any(e => e.Error == "not_found"))
            {
                return NotFound(new ErrorResponse
                {
                    Error = "NotFound",
                    Message = $"Record with id '{id}' not found in table '{table}'",
                    Code = "NOT_FOUND"
                });
            }

            return Ok(MapToFinalizeResultApi(result));
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
    /// Finalize (validate) multiple records that were saved in draft mode.
    /// </summary>
    [HttpPost("data/{table}/finalize")]
    [ProducesResponseType(typeof(FinalizeApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FinalizeBatch(
        string table,
        [FromBody] FinalizeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenantId = GetTenantId();

            if (request.RecordIds is null || request.RecordIds.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "At least one record ID is required",
                    Code = "EMPTY_RECORD_IDS"
                });
            }

            var results = await _transactionService.FinalizeBatchAsync(
                tenantId, table, request.RecordIds, cancellationToken);

            var validCount = results.Count(r => r.Success);
            var errorCount = results.Count(r => !r.Success);

            return Ok(new FinalizeApiResponse
            {
                Results = results.Select(MapToFinalizeResultApi).ToList(),
                ValidCount = validCount,
                ErrorCount = errorCount
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

    #region Mapping Methods

    private static TransactionRequest MapToTransactionRequest(TransactionApiRequest apiRequest)
    {
        return new TransactionRequest
        {
            Operations = apiRequest.Operations.Select(MapToTransactionOperation).ToList(),
            TimeoutMs = apiRequest.TimeoutMs,
            ReturnFullRecords = apiRequest.ReturnFullRecords
        };
    }

    private static TransactionOperation MapToTransactionOperation(TransactionOperationApiRequest apiOp)
    {
        WriteOptions? options = null;
        if (apiOp.Mode?.Equals("draft", StringComparison.OrdinalIgnoreCase) == true)
        {
            options = WriteOptions.DraftMode;
        }

        return new TransactionOperation
        {
            Method = apiOp.Method,
            Table = apiOp.Table,
            Data = apiOp.Data,
            Id = apiOp.Id,
            Ref = apiOp.Ref,
            KeyColumns = apiOp.KeyColumns,
            Options = options
        };
    }

    private static TransactionApiResponse MapToTransactionResponse(TransactionResult result)
    {
        return new TransactionApiResponse
        {
            Success = result.Success,
            Results = result.Results.Select(MapToTransactionOperationResult).ToList(),
            Error = result.Error,
            FailedOperationIndex = result.FailedOperationIndex
        };
    }

    private static TransactionOperationApiResult MapToTransactionOperationResult(TransactionOperationResult result)
    {
        return new TransactionOperationApiResult
        {
            Index = result.Index,
            Success = result.Success,
            Ref = result.Ref,
            Id = result.Id,
            Data = result.Data,
            AffectedRows = result.AffectedRows,
            Error = result.Error,
            ValidationErrors = result.ValidationErrors?.Select(e => new ValidationErrorApi
            {
                Field = e.Field,
                Code = e.Code,
                Message = e.Message
            }).ToList()
        };
    }

    private static FinalizeResultApi MapToFinalizeResultApi(FinalizeResult result)
    {
        return new FinalizeResultApi
        {
            RecordId = result.RecordId,
            Success = result.Success,
            NewState = result.NewState.ToString().ToLowerInvariant(),
            Errors = result.Errors.Select(e => new RowValidationErrorApi
            {
                Column = e.Column,
                Error = e.Error,
                Message = e.Message,
                AttemptedValue = e.AttemptedValue
            }).ToList(),
            Data = result.Data
        };
    }

    #endregion
}
