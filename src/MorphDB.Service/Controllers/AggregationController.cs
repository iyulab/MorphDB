using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for aggregation operations (COUNT, SUM, AVG, MIN, MAX with GROUP BY).
/// </summary>
[ApiController]
[Route("api/data")]
public sealed class AggregationController : ControllerBase
{
    private readonly IAggregationService _aggregationService;

    public AggregationController(IAggregationService aggregationService)
    {
        _aggregationService = aggregationService;
    }

    private Guid GetProjectId()
    {
        if (Request.Headers.TryGetValue("X-Project-Id", out var projectIdHeader) &&
            Guid.TryParse(projectIdHeader.FirstOrDefault(), out var projectId))
        {
            return projectId;
        }

        throw new InvalidOperationException("X-Project-Id header is required");
    }

    /// <summary>
    /// Perform aggregation on a table with optional grouping.
    /// </summary>
    /// <remarks>
    /// Supports COUNT, SUM, AVG, MIN, MAX with optional GROUP BY, HAVING, and ORDER BY.
    ///
    /// Example request:
    /// ```json
    /// {
    ///   "aggregations": [
    ///     { "function": "count", "alias": "total_count" },
    ///     { "function": "sum", "column": "amount", "alias": "total_amount" },
    ///     { "function": "avg", "column": "price", "alias": "avg_price" }
    ///   ],
    ///   "groupBy": ["category", "status"],
    ///   "filter": [
    ///     { "column": "status", "operator": "eq", "value": "active" }
    ///   ],
    ///   "having": [
    ///     { "alias": "total_count", "operator": "gte", "value": 10 }
    ///   ],
    ///   "orderBy": [
    ///     { "column": "total_amount", "direction": "desc" }
    ///   ],
    ///   "limit": 100
    /// }
    /// ```
    /// </remarks>
    [HttpPost("{table}/aggregate")]
    [ProducesResponseType(typeof(AggregationApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Aggregate(
        string table,
        [FromBody] AggregationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();

            // Validate request
            if (request.Aggregations.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "BadRequest",
                    Message = "At least one aggregation is required",
                    Code = "AGGREGATION_REQUIRED"
                });
            }

            // Execute aggregation
            var result = await _aggregationService.AggregateAsync(
                projectId,
                table,
                request.ToModel(),
                cancellationToken);

            return Ok(AggregationApiResponse.FromResult(result));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("X-Project-Id"))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = ex.Message,
                Code = "MISSING_PROJECT"
            });
        }
        catch (TableNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
        catch (ColumnNotFoundException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = ex.Message,
                Code = "COLUMN_NOT_FOUND"
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = ex.Message,
                Code = "INVALID_ARGUMENT"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = ex.Message
            });
        }
    }
}
