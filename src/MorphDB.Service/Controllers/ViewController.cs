using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;
using CoreExceptions = MorphDB.Core.Exceptions;

namespace MorphDB.Service.Controllers;

internal static partial class ViewControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Created view {ViewName} for project {ProjectId}")]
    public static partial void ViewCreated(ILogger logger, string viewName, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Updated view {ViewName} for project {ProjectId}")]
    public static partial void ViewUpdated(ILogger logger, string viewName, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Deleted view {ViewName} for project {ProjectId}")]
    public static partial void ViewDeleted(ILogger logger, string viewName, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Refreshed materialized view {ViewName}")]
    public static partial void ViewRefreshed(ILogger logger, string viewName);
}

/// <summary>
/// View management API endpoints.
/// </summary>
[ApiController]
[Route("api/views")]
[Produces("application/json")]
[RequireProject]
public sealed class ViewController : ControllerBase
{
    private readonly IViewManager _viewManager;
    private readonly ILogger<ViewController> _logger;
    private readonly IProjectContextAccessor _projectContext;

    public ViewController(
        IViewManager viewManager,
        ILogger<ViewController> logger,
        IProjectContextAccessor projectContext)
    {
        _viewManager = viewManager;
        _logger = logger;
        _projectContext = projectContext;
    }

    private Guid GetProjectId() => _projectContext.ProjectId;

    #region View CRUD

    /// <summary>
    /// Creates a new view.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ViewApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateView(
        [FromBody] CreateViewApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var createRequest = MapToCreateViewRequest(projectId, request);
            var view = await _viewManager.CreateViewAsync(createRequest, cancellationToken);
            var response = ViewApiResponse.FromMetadata(view);

            ViewControllerLogs.ViewCreated(_logger, view.LogicalName, projectId);
            return CreatedAtAction(nameof(GetView), new { name = view.LogicalName }, response);
        }
        catch (CoreExceptions.DuplicateNameException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = "Conflict",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (CoreExceptions.NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (CoreExceptions.ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Lists all views for the project.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ViewApiResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListViews(CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var views = await _viewManager.ListViewsAsync(projectId, cancellationToken);
        var response = views.Select(ViewApiResponse.FromMetadata).ToList();
        return Ok(response);
    }

    /// <summary>
    /// Gets a view by name.
    /// </summary>
    [HttpGet("{name}")]
    [ProducesResponseType(typeof(ViewApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetView(string name, CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var view = await _viewManager.GetViewAsync(projectId, name, cancellationToken);

        if (view == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"View '{name}' not found.",
                Code = "VIEW_NOT_FOUND"
            });
        }

        return Ok(ViewApiResponse.FromMetadata(view));
    }

    /// <summary>
    /// Updates a view.
    /// </summary>
    [HttpPatch("{name}")]
    [ProducesResponseType(typeof(ViewApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateView(
        string name,
        [FromBody] UpdateViewApiRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var existingView = await _viewManager.GetViewAsync(projectId, name, cancellationToken);
            if (existingView == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "NotFound",
                    Message = $"View '{name}' not found.",
                    Code = "VIEW_NOT_FOUND"
                });
            }

            var updateRequest = MapToUpdateViewRequest(existingView.ViewId, request, existingView.Definition);
            var updatedView = await _viewManager.UpdateViewAsync(updateRequest, cancellationToken);
            var response = ViewApiResponse.FromMetadata(updatedView);

            ViewControllerLogs.ViewUpdated(_logger, updatedView.LogicalName, projectId);
            return Ok(response);
        }
        catch (CoreExceptions.NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (CoreExceptions.ValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Deletes a view.
    /// </summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteView(string name, CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();
            var existingView = await _viewManager.GetViewAsync(projectId, name, cancellationToken);
            if (existingView == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "NotFound",
                    Message = $"View '{name}' not found.",
                    Code = "VIEW_NOT_FOUND"
                });
            }

            await _viewManager.DeleteViewAsync(existingView.ViewId, cancellationToken);

            ViewControllerLogs.ViewDeleted(_logger, name, projectId);
            return NoContent();
        }
        catch (CoreExceptions.NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    #endregion

    #region Materialized View Operations

    /// <summary>
    /// Refreshes a materialized view.
    /// </summary>
    [HttpPost("{name}/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshMaterializedView(
        string name,
        [FromQuery] bool concurrent = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var existingView = await _viewManager.GetViewAsync(projectId, name, cancellationToken);
            if (existingView == null)
            {
                return NotFound(new ErrorResponse
                {
                    Error = "NotFound",
                    Message = $"View '{name}' not found.",
                    Code = "VIEW_NOT_FOUND"
                });
            }

            if (!existingView.IsMaterialized)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "ValidationError",
                    Message = $"View '{name}' is not a materialized view.",
                    Code = "NOT_MATERIALIZED"
                });
            }

            await _viewManager.RefreshMaterializedViewAsync(existingView.ViewId, concurrent, cancellationToken);

            ViewControllerLogs.ViewRefreshed(_logger, name);
            return NoContent();
        }
        catch (CoreExceptions.NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Checks if a materialized view is stale.
    /// </summary>
    [HttpGet("{name}/stale")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckMaterializedViewStale(
        string name,
        CancellationToken cancellationToken)
    {
        var projectId = GetProjectId();
        var existingView = await _viewManager.GetViewAsync(projectId, name, cancellationToken);
        if (existingView == null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = $"View '{name}' not found.",
                Code = "VIEW_NOT_FOUND"
            });
        }

        if (!existingView.IsMaterialized)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = $"View '{name}' is not a materialized view.",
                Code = "NOT_MATERIALIZED"
            });
        }

        var isStale = await _viewManager.IsMaterializedViewStaleAsync(existingView.ViewId, cancellationToken);
        return Ok(new { isStale, lastRefreshedAt = existingView.LastRefreshedAt });
    }

    #endregion

    #region View Data Query

    /// <summary>
    /// Queries data from a view.
    /// </summary>
    [HttpGet("{name}/data")]
    [ProducesResponseType(typeof(ViewQueryApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryViewData(
        string name,
        [FromQuery] ViewQueryApiParameters query,
        CancellationToken cancellationToken)
    {
        try
        {
            var projectId = GetProjectId();

            var queryRequest = new ViewQueryRequest
            {
                ProjectId = projectId,
                ViewName = name,
                Columns = ParseColumns(query.Select),
                Filters = ParseFilters(query.Filter),
                OrderBy = ParseOrderBy(query.OrderBy),
                Skip = query.Skip,
                Take = query.Take ?? 50
            };

            var result = await _viewManager.QueryViewAsync(queryRequest, cancellationToken);

            return Ok(new ViewQueryApiResponse
            {
                Data = result.Data,
                TotalCount = result.TotalCount,
                HasMore = result.HasMore
            });
        }
        catch (CoreExceptions.NotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    #endregion

    #region Mapping Helpers

    private static CreateViewRequest MapToCreateViewRequest(Guid projectId, CreateViewApiRequest request)
    {
        return new CreateViewRequest
        {
            ProjectId = projectId,
            Name = request.Name,
            Definition = new ViewDefinition
            {
                BaseTable = request.BaseTable,
                Columns = request.Columns.Select(c => new ViewColumnSpec
                {
                    Source = c.Source,
                    Expression = c.Expression,
                    Alias = c.Alias,
                    DataType = c.DataType != null ? ApiModelExtensions.ParseDataType(c.DataType) : null,
                    Aggregation = c.Aggregation != null ? ParseAggregation(c.Aggregation) : null
                }).ToList(),
                Joins = request.Joins?.Select(j => new ViewJoinSpec
                {
                    Table = j.Table,
                    Alias = j.Alias,
                    JoinType = ParseJoinType(j.JoinType),
                    Condition = j.Condition
                }).ToList() ?? [],
                Filters = request.Filters?.Select(f => new ViewFilterSpec
                {
                    Field = f.Field,
                    Operator = ApiModelExtensions.ParseFilterOperator(f.Operator),
                    Value = f.Value,
                    LogicalOp = ParseLogicalOperator(f.LogicalOp)
                }).ToList() ?? [],
                GroupBy = request.GroupBy?.ToList() ?? [],
                OrderBy = request.OrderBy?.Select(o => new ViewOrderSpec
                {
                    Column = o.Column,
                    Descending = o.Descending,
                    NullOrdering = ParseNullOrdering(o.NullOrdering)
                }).ToList() ?? [],
                Limit = request.Limit,
                Distinct = request.Distinct
            },
            IsMaterialized = request.Materialized,
            RefreshPolicy = request.RefreshPolicy != null
                ? ParseRefreshPolicy(request.RefreshPolicy)
                : MaterializedViewRefreshPolicy.OnDemand,
            RefreshSchedule = request.RefreshSchedule,
            Description = request.Description
        };
    }

    private static UpdateViewRequest MapToUpdateViewRequest(
        Guid viewId,
        UpdateViewApiRequest request,
        ViewDefinition existingDefinition)
    {
        ViewDefinition? newDefinition = null;

        if (request.Columns != null || request.Joins != null || request.Filters != null ||
            request.GroupBy != null || request.OrderBy != null || request.Limit.HasValue ||
            request.Distinct.HasValue)
        {
            newDefinition = new ViewDefinition
            {
                BaseTable = existingDefinition.BaseTable,
                Columns = request.Columns?.Select(c => new ViewColumnSpec
                {
                    Source = c.Source,
                    Expression = c.Expression,
                    Alias = c.Alias,
                    DataType = c.DataType != null ? ApiModelExtensions.ParseDataType(c.DataType) : null,
                    Aggregation = c.Aggregation != null ? ParseAggregation(c.Aggregation) : null
                }).ToList() ?? existingDefinition.Columns.ToList(),
                Joins = request.Joins?.Select(j => new ViewJoinSpec
                {
                    Table = j.Table,
                    Alias = j.Alias,
                    JoinType = ParseJoinType(j.JoinType),
                    Condition = j.Condition
                }).ToList() ?? existingDefinition.Joins.ToList(),
                Filters = request.Filters?.Select(f => new ViewFilterSpec
                {
                    Field = f.Field,
                    Operator = ApiModelExtensions.ParseFilterOperator(f.Operator),
                    Value = f.Value,
                    LogicalOp = ParseLogicalOperator(f.LogicalOp)
                }).ToList() ?? existingDefinition.Filters.ToList(),
                GroupBy = request.GroupBy?.ToList() ?? existingDefinition.GroupBy.ToList(),
                OrderBy = request.OrderBy?.Select(o => new ViewOrderSpec
                {
                    Column = o.Column,
                    Descending = o.Descending,
                    NullOrdering = ParseNullOrdering(o.NullOrdering)
                }).ToList() ?? existingDefinition.OrderBy.ToList(),
                Limit = request.Limit ?? existingDefinition.Limit,
                Distinct = request.Distinct ?? existingDefinition.Distinct
            };
        }

        return new UpdateViewRequest
        {
            ViewId = viewId,
            Name = request.Name,
            Definition = newDefinition,
            RefreshPolicy = request.RefreshPolicy != null
                ? ParseRefreshPolicy(request.RefreshPolicy)
                : null,
            RefreshSchedule = request.RefreshSchedule,
            Description = request.Description
        };
    }

    private static List<string>? ParseColumns(string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
            return null;

        return select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<ViewFilterSpec>? ParseFilters(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var filters = new List<ViewFilterSpec>();
        var parts = filter.Split(" AND ", StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0)
            {
                var secondColonIndex = part.IndexOf(':', colonIndex + 1);
                if (secondColonIndex > 0)
                {
                    var field = part[..colonIndex];
                    var op = part[(colonIndex + 1)..secondColonIndex];
                    var value = part[(secondColonIndex + 1)..];

                    filters.Add(new ViewFilterSpec
                    {
                        Field = field,
                        Operator = ApiModelExtensions.ParseFilterOperator(op),
                        Value = value,
                        LogicalOp = LogicalOperator.And
                    });
                }
            }
        }

        return filters.Count > 0 ? filters : null;
    }

    private static List<ViewOrderSpec>? ParseOrderBy(string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return null;

        var orders = new List<ViewOrderSpec>();
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            var colonIndex = part.IndexOf(':');
            if (colonIndex > 0)
            {
                var column = part[..colonIndex];
                var direction = part[(colonIndex + 1)..].ToLowerInvariant();

                orders.Add(new ViewOrderSpec
                {
                    Column = column,
                    Descending = direction == "desc"
                });
            }
            else
            {
                orders.Add(new ViewOrderSpec
                {
                    Column = part,
                    Descending = false
                });
            }
        }

        return orders.Count > 0 ? orders : null;
    }

    private static AggregationFunction? ParseAggregation(string aggregation)
    {
        return aggregation.ToLowerInvariant() switch
        {
            "count" => AggregationFunction.Count,
            "sum" => AggregationFunction.Sum,
            "avg" => AggregationFunction.Avg,
            "min" => AggregationFunction.Min,
            "max" => AggregationFunction.Max,
            "arrayagg" or "array_agg" => AggregationFunction.ArrayAgg,
            "stringagg" or "string_agg" => AggregationFunction.StringAgg,
            "first" => AggregationFunction.First,
            "last" => AggregationFunction.Last,
            _ => null
        };
    }

    private static ViewJoinType ParseJoinType(string joinType)
    {
        return joinType.ToLowerInvariant() switch
        {
            "inner" => ViewJoinType.Inner,
            "left" => ViewJoinType.Left,
            "right" => ViewJoinType.Right,
            "full" => ViewJoinType.Full,
            "cross" => ViewJoinType.Cross,
            _ => ViewJoinType.Left
        };
    }

    private static LogicalOperator ParseLogicalOperator(string logicalOp)
    {
        return logicalOp.ToLowerInvariant() switch
        {
            "or" => LogicalOperator.Or,
            _ => LogicalOperator.And
        };
    }

    private static NullOrdering ParseNullOrdering(string nullOrdering)
    {
        return nullOrdering.ToLowerInvariant() switch
        {
            "first" => NullOrdering.First,
            _ => NullOrdering.Last
        };
    }

    private static MaterializedViewRefreshPolicy ParseRefreshPolicy(string policy)
    {
        return policy.ToLowerInvariant() switch
        {
            "scheduled" => MaterializedViewRefreshPolicy.Scheduled,
            "incremental" => MaterializedViewRefreshPolicy.Incremental,
            _ => MaterializedViewRefreshPolicy.OnDemand
        };
    }

    #endregion
}
