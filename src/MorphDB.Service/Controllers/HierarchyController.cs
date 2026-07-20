using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Controllers;

/// <summary>
/// API endpoints for querying hierarchical (tree-structured) data.
/// </summary>
[ApiController]
[Route("api/hierarchy")]
[Produces("application/json")]
public sealed class HierarchyController : ControllerBase
{
    private readonly IHierarchyQueryService _hierarchyService;
    private readonly IProjectContextAccessor _projectContext;

    public HierarchyController(
        IHierarchyQueryService hierarchyService,
        IProjectContextAccessor projectContext)
    {
        _hierarchyService = hierarchyService;
        _projectContext = projectContext;
    }

    private Guid GetProjectId()
    {
        var projectId = _projectContext.ProjectIdOrNull;
        if (!projectId.HasValue || projectId.Value == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Valid API key is required");
        }
        return projectId.Value;
    }

    /// <summary>
    /// Gets ancestors (parent chain) of a record.
    /// </summary>
    [HttpGet("{table}/{id:guid}/ancestors")]
    [ProducesResponseType(typeof(HierarchyApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAncestors(
        string table,
        Guid id,
        [FromQuery] string parentColumn = "_parent_id",
        [FromQuery] int? maxDepth = null,
        [FromQuery] bool includeSelf = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var request = new HierarchyQueryRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = parentColumn,
                RecordId = id,
                MaxDepth = maxDepth,
                IncludeSelf = includeSelf
            };

            var result = await _hierarchyService.GetAncestorsAsync(request, cancellationToken);
            return Ok(MapToApiResponse(result));
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Gets descendants (children tree) of a record.
    /// </summary>
    [HttpGet("{table}/{id:guid}/descendants")]
    [ProducesResponseType(typeof(HierarchyApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDescendants(
        string table,
        Guid id,
        [FromQuery] string parentColumn = "_parent_id",
        [FromQuery] int? maxDepth = null,
        [FromQuery] bool includeSelf = false,
        [FromQuery] string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var request = new HierarchyQueryRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = parentColumn,
                RecordId = id,
                MaxDepth = maxDepth,
                IncludeSelf = includeSelf,
                OrderBy = orderBy
            };

            var result = await _hierarchyService.GetDescendantsAsync(request, cancellationToken);
            return Ok(MapToApiResponse(result));
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Gets the full path from root to the specified record.
    /// </summary>
    [HttpGet("{table}/{id:guid}/path")]
    [ProducesResponseType(typeof(HierarchyApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPath(
        string table,
        Guid id,
        [FromQuery] string parentColumn = "_parent_id",
        [FromQuery] int? maxDepth = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var request = new HierarchyQueryRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = parentColumn,
                RecordId = id,
                MaxDepth = maxDepth,
                IncludeSelf = true
            };

            var result = await _hierarchyService.GetPathToRootAsync(request, cancellationToken);
            return Ok(MapToApiResponse(result));
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Gets siblings of a record (records with the same parent).
    /// </summary>
    [HttpGet("{table}/{id:guid}/siblings")]
    [ProducesResponseType(typeof(HierarchyApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSiblings(
        string table,
        Guid id,
        [FromQuery] string parentColumn = "_parent_id",
        [FromQuery] bool includeSelf = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var request = new HierarchyQueryRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = parentColumn,
                RecordId = id,
                IncludeSelf = includeSelf
            };

            var result = await _hierarchyService.GetSiblingsAsync(request, cancellationToken);
            return Ok(MapToApiResponse(result));
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Gets the full subtree rooted at the specified record.
    /// </summary>
    [HttpGet("{table}/{id:guid}/subtree")]
    [ProducesResponseType(typeof(HierarchyApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubtree(
        string table,
        Guid id,
        [FromQuery] string parentColumn = "_parent_id",
        [FromQuery] int? maxDepth = null,
        [FromQuery] string? orderBy = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var request = new HierarchyQueryRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = parentColumn,
                RecordId = id,
                MaxDepth = maxDepth,
                IncludeSelf = true,
                OrderBy = orderBy
            };

            var result = await _hierarchyService.GetSubtreeAsync(request, cancellationToken);
            return Ok(MapToApiResponse(result));
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Checks if setting a new parent would create a cycle.
    /// </summary>
    [HttpPost("{table}/check-cycle")]
    [ProducesResponseType(typeof(CycleCheckApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckCycle(
        string table,
        [FromBody] CycleCheckApiRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var checkRequest = new CycleCheckRequest
            {
                ProjectId = projectId,
                TableName = table,
                ParentColumn = request.ParentColumn ?? "_parent_id",
                RecordId = request.RecordId,
                NewParentId = request.NewParentId
            };

            var wouldCycle = await _hierarchyService.WouldCreateCycleAsync(checkRequest, cancellationToken);
            return Ok(new CycleCheckApiResponse { WouldCreateCycle = wouldCycle });
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    /// <summary>
    /// Detects all cycles in the hierarchy.
    /// </summary>
    [HttpGet("{table}/detect-cycles")]
    [ProducesResponseType(typeof(CycleDetectionApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetectCycles(
        string table,
        [FromQuery] string parentColumn = "_parent_id",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = GetProjectId();
            var result = await _hierarchyService.DetectCyclesAsync(
                projectId, table, parentColumn, cancellationToken);

            return Ok(new CycleDetectionApiResponse
            {
                HasCycles = result.HasCycles,
                CyclicRecordIds = result.CyclicRecordIds,
                CycleDescriptions = result.CycleDescriptions
            });
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = ex.Message,
                Code = "TABLE_NOT_FOUND"
            });
        }
    }

    private static HierarchyApiResponse MapToApiResponse(HierarchyQueryResult result)
    {
        return new HierarchyApiResponse
        {
            Records = result.Records.Select(r => new HierarchyRecordApiResponse
            {
                Data = r.Data,
                Depth = r.Depth,
                Path = r.Path
            }).ToList(),
            TotalCount = result.TotalCount,
            MaxDepth = result.MaxDepth,
            ReachedMaxDepth = result.ReachedMaxDepth
        };
    }
}

#region API Models

public sealed class HierarchyApiResponse
{
    public required IReadOnlyList<HierarchyRecordApiResponse> Records { get; init; }
    public int TotalCount { get; init; }
    public int MaxDepth { get; init; }
    public bool ReachedMaxDepth { get; init; }
}

public sealed class HierarchyRecordApiResponse
{
    public required IDictionary<string, object?> Data { get; init; }
    public int Depth { get; init; }
    public IReadOnlyList<Guid>? Path { get; init; }
}

public sealed class CycleCheckApiRequest
{
    public Guid RecordId { get; init; }
    public Guid NewParentId { get; init; }
    public string? ParentColumn { get; init; }
}

public sealed class CycleCheckApiResponse
{
    public bool WouldCreateCycle { get; init; }
}

public sealed class CycleDetectionApiResponse
{
    public bool HasCycles { get; init; }
    public IReadOnlyList<Guid> CyclicRecordIds { get; init; } = [];
    public IReadOnlyList<string> CycleDescriptions { get; init; } = [];
}

#endregion
