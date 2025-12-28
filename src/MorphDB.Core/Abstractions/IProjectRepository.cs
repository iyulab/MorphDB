using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Repository for managing project entities in the global control plane.
/// Projects are stored in the morphdb schema.
/// </summary>
public interface IProjectRepository
{
    /// <summary>
    /// Creates a new project record.
    /// </summary>
    Task<Project> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project by its ID.
    /// </summary>
    Task<Project?> GetByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a project by its slug.
    /// </summary>
    Task<Project?> GetBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all projects, optionally filtered by organization.
    /// </summary>
    Task<IReadOnlyList<Project>> ListAsync(
        Guid? organizationId = null,
        ProjectStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a project's settings or status.
    /// </summary>
    Task<Project> UpdateAsync(
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the project status.
    /// </summary>
    Task UpdateStatusAsync(
        Guid projectId,
        ProjectStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a project by setting status to Deleted.
    /// </summary>
    Task DeleteAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a slug is available.
    /// </summary>
    Task<bool> IsSlugAvailableAsync(
        string slug,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets total count of projects, optionally filtered.
    /// </summary>
    Task<int> CountAsync(
        Guid? organizationId = null,
        ProjectStatus? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new project.
/// </summary>
public sealed record CreateProjectRequest
{
    /// <summary>
    /// Optional pre-defined project ID. If null, a new GUID will be generated.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Optional organization ID for hierarchical multi-tenancy.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Human-readable project name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe unique identifier. If null, will be generated from name.
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// Optional project settings.
    /// </summary>
    public ProjectSettings? Settings { get; init; }
}

/// <summary>
/// Request to update an existing project.
/// </summary>
public sealed record UpdateProjectRequest
{
    /// <summary>
    /// The project ID to update.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// New project name (optional).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Updated settings (optional). Null means no change.
    /// </summary>
    public ProjectSettings? Settings { get; init; }
}
