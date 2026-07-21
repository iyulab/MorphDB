using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// High-level service for project lifecycle management.
/// Coordinates between project repository and schema layer service.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Creates a new project with its associated database schemas.
    /// This is an atomic operation - if schema creation fails, the project is not created.
    /// </summary>
    Task<Project> CreateProjectAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project by its ID.
    /// </summary>
    Task<Project?> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project by its slug.
    /// </summary>
    Task<Project?> GetProjectBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all projects with optional filters.
    /// </summary>
    Task<IReadOnlyList<Project>> ListProjectsAsync(
        ProjectStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a project's settings or name.
    /// </summary>
    Task<Project> UpdateProjectAsync(
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a project and its associated schemas.
    /// This is a destructive operation.
    /// </summary>
    Task DeleteProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets project statistics including schema sizes.
    /// </summary>
    Task<ProjectSchemaStats> GetProjectStatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates project health and schema integrity.
    /// </summary>
    Task<SchemaHealthReport> ValidateProjectHealthAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
