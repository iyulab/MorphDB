using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// LoggerMessage delegates for ProjectController.
/// </summary>
internal static partial class ProjectControllerLogs
{
    [LoggerMessage(LogLevel.Information, "Creating project: {ProjectName}")]
    public static partial void CreatingProject(ILogger logger, string projectName);

    [LoggerMessage(LogLevel.Information, "Project created: {ProjectId} ({Slug})")]
    public static partial void ProjectCreated(ILogger logger, Guid projectId, string slug);

    [LoggerMessage(LogLevel.Information, "Listing projects (status={Status})")]
    public static partial void ListingProjects(ILogger logger, string? status);

    [LoggerMessage(LogLevel.Information, "Getting project: {ProjectId}")]
    public static partial void GettingProject(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting project by slug: {Slug}")]
    public static partial void GettingProjectBySlug(ILogger logger, string slug);

    [LoggerMessage(LogLevel.Information, "Updating project: {ProjectId}")]
    public static partial void UpdatingProject(ILogger logger, Guid projectId);


    [LoggerMessage(LogLevel.Warning, "Deleting project: {ProjectId}")]
    public static partial void DeletingProject(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Getting project stats: {ProjectId}")]
    public static partial void GettingProjectStats(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Information, "Validating project health: {ProjectId}")]
    public static partial void ValidatingProjectHealth(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Project operation failed: {Error}")]
    public static partial void ProjectOperationFailed(ILogger logger, string error, Exception exception);
}

/// <summary>
/// REST API controller for project lifecycle management.
/// Handles project creation, updates, status changes, and deletion.
/// </summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
public sealed class ProjectController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(
        IProjectService projectService,
        ILogger<ProjectController> logger)
    {
        _projectService = projectService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new project with its associated database schemas.
    /// </summary>
    /// <param name="request">The project creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created project.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectApiResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateProject(
        [FromBody] CreateProjectApiRequest request,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.CreatingProject(_logger, request.Name);

        try
        {
            var coreRequest = new CreateProjectRequest
            {
                ProjectId = request.ProjectId,
                Name = request.Name,
                Slug = request.Slug,
                Settings = request.Settings?.ToModel()
            };

            var project = await _projectService.CreateProjectAsync(coreRequest, cancellationToken);
            var response = ProjectApiResponse.FromModel(project);

            ProjectControllerLogs.ProjectCreated(_logger, project.ProjectId, project.Slug);

            return CreatedAtAction(
                nameof(GetProject),
                new { id = project.ProjectId },
                response);
        }
        catch (DuplicateSlugException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (DuplicateProjectIdException ex)
        {
            return Conflict(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (MorphDbException ex)
        {
            ProjectControllerLogs.ProjectOperationFailed(_logger, ex.ErrorCode, ex);
            return BadRequest(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Lists all projects with optional filters.
    /// </summary>
    /// <param name="parameters">Query parameters for filtering and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of projects.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProjectApiResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListProjects(
        [FromQuery] ProjectQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.ListingProjects(_logger, parameters.Status);

        var pageSize = Math.Clamp(parameters.PageSize, 1, 100);
        var page = Math.Max(1, parameters.Page);
        var offset = (page - 1) * pageSize;

        ProjectStatus? status = null;
        if (!string.IsNullOrEmpty(parameters.Status) &&
            Enum.TryParse<ProjectStatus>(parameters.Status, ignoreCase: true, out var parsedStatus))
        {
            status = parsedStatus;
        }

        var projects = await _projectService.ListProjectsAsync(
            status,
            offset,
            pageSize,
            cancellationToken);

        var totalCount = await _projectService.CountProjectsAsync(status, cancellationToken);

        var response = new PagedResponse<ProjectApiResponse>
        {
            Data = projects.Select(ProjectApiResponse.FromModel).ToList(),
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
            }
        };

        return Ok(response);
    }

    /// <summary>
    /// Gets a project by its ID.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The project.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProjectApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(
        Guid id,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.GettingProject(_logger, id);

        var project = await _projectService.GetProjectAsync(id, cancellationToken);

        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "PROJECT_NOT_FOUND",
                Message = $"Project with ID '{id}' not found.",
                Code = "PROJECT_NOT_FOUND"
            });
        }

        return Ok(ProjectApiResponse.FromModel(project));
    }

    /// <summary>
    /// Gets a project by its slug.
    /// </summary>
    /// <param name="slug">The project slug.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The project.</returns>
    [HttpGet("slug/{slug}")]
    [ProducesResponseType(typeof(ProjectApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectBySlug(
        string slug,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.GettingProjectBySlug(_logger, slug);

        var project = await _projectService.GetProjectBySlugAsync(slug, cancellationToken);

        if (project is null)
        {
            return NotFound(new ErrorResponse
            {
                Error = "PROJECT_NOT_FOUND",
                Message = $"Project with slug '{slug}' not found.",
                Code = "PROJECT_NOT_FOUND"
            });
        }

        return Ok(ProjectApiResponse.FromModel(project));
    }

    /// <summary>
    /// Updates a project's settings or name.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="request">The update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated project.</returns>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ProjectApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProject(
        Guid id,
        [FromBody] UpdateProjectApiRequest request,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.UpdatingProject(_logger, id);

        try
        {
            var coreRequest = new UpdateProjectRequest
            {
                ProjectId = id,
                Name = request.Name,
                Settings = request.Settings?.ToModel()
            };

            var project = await _projectService.UpdateProjectAsync(coreRequest, cancellationToken);
            return Ok(ProjectApiResponse.FromModel(project));
        }
        catch (ProjectNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (MorphDbException ex)
        {
            ProjectControllerLogs.ProjectOperationFailed(_logger, ex.ErrorCode, ex);
            return BadRequest(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Deletes a project and its associated schemas.
    /// This is a destructive operation.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteProject(
        Guid id,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.DeletingProject(_logger, id);

        try
        {
            await _projectService.DeleteProjectAsync(id, cancellationToken);
            return NoContent();
        }
        catch (ProjectNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
        catch (MorphDbException ex)
        {
            ProjectControllerLogs.ProjectOperationFailed(_logger, ex.ErrorCode, ex);
            return StatusCode(500, new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Gets project statistics including schema sizes.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project statistics.</returns>
    [HttpGet("{id:guid}/stats")]
    [ProducesResponseType(typeof(ProjectStatsApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectStats(
        Guid id,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.GettingProjectStats(_logger, id);

        try
        {
            var stats = await _projectService.GetProjectStatsAsync(id, cancellationToken);
            return Ok(ProjectStatsApiResponse.FromModel(stats));
        }
        catch (ProjectNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }

    /// <summary>
    /// Validates project health and schema integrity.
    /// </summary>
    /// <param name="id">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Schema health report.</returns>
    [HttpGet("{id:guid}/health")]
    [ProducesResponseType(typeof(SchemaHealthApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateProjectHealth(
        Guid id,
        CancellationToken cancellationToken)
    {
        ProjectControllerLogs.ValidatingProjectHealth(_logger, id);

        try
        {
            var report = await _projectService.ValidateProjectHealthAsync(id, cancellationToken);
            return Ok(SchemaHealthApiResponse.FromModel(report));
        }
        catch (ProjectNotFoundException ex)
        {
            return NotFound(new ErrorResponse
            {
                Error = ex.ErrorCode,
                Message = ex.Message,
                Code = ex.ErrorCode
            });
        }
    }
}
