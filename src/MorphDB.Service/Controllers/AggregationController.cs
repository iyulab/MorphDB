using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for aggregation operations (COUNT, SUM, AVG, MIN, MAX with GROUP BY).
/// </summary>
[ApiController]
[Route("api/data")]
[RequireProject]
public sealed class AggregationController : ControllerBase
{
    private readonly IAggregationService _aggregationService;
    private readonly IProjectContextAccessor _projectContext;
    private readonly ILogger<AggregationController> _logger;

    public AggregationController(
        IAggregationService aggregationService,
        IProjectContextAccessor projectContext,
        ILogger<AggregationController> logger)
    {
        _aggregationService = aggregationService;
        _projectContext = projectContext;
        _logger = logger;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

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
}
