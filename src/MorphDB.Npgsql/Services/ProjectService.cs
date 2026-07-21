using Microsoft.Extensions.Logging;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// PostgreSQL implementation of project lifecycle management.
/// Coordinates between project repository and schema layer service.
/// </summary>
public sealed partial class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly ISchemaLayerService _schemaLayerService;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        IProjectRepository projectRepository,
        ISchemaLayerService schemaLayerService,
        ILogger<ProjectService> logger)
    {
        _projectRepository = projectRepository;
        _schemaLayerService = schemaLayerService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Project> CreateProjectAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        LogCreatingProject(_logger, request.Name);

        // Step 1: Create project record in Provisioning status
        var project = await _projectRepository.CreateAsync(request, cancellationToken);

        try
        {
            // Step 2: Provision database schemas
            await _schemaLayerService.ProvisionProjectSchemasAsync(project.ProjectId, cancellationToken);

            // Step 3: Update status to Active
            await _projectRepository.UpdateStatusAsync(project.ProjectId, ProjectStatus.Active, cancellationToken);

            LogProjectCreated(_logger, project.ProjectId, project.Slug);

            // Refusing the null-forgiveness here: if the row we just created cannot be read back,
            // something is wrong enough that returning null-as-Project would only defer the crash.
            return await _projectRepository.GetByIdAsync(project.ProjectId, cancellationToken)
                ?? throw new MorphDbException(
                    "PROJECT_CREATION_FAILED",
                    $"Project '{project.ProjectId}' was created but cannot be read back.");
        }
        catch (Exception ex)
        {
            LogProjectProvisioningFailed(_logger, project.ProjectId, ex);

            // Cleanup: Delete the project record since schema provisioning failed
            try
            {
                await _projectRepository.DeleteAsync(project.ProjectId, cancellationToken);
                LogProjectCleanedUp(_logger, project.ProjectId);
            }
            catch (Exception cleanupEx)
            {
                LogProjectCleanupFailed(_logger, project.ProjectId, cleanupEx);
            }

            throw new MorphDbException(
                "PROJECT_CREATION_FAILED",
                $"Failed to create project '{request.Name}': {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Project?> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        return await _projectRepository.GetByIdAsync(projectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Project?> GetProjectBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        return await _projectRepository.GetBySlugAsync(slug, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Project>> ListProjectsAsync(
        ProjectStatus? status = null,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _projectRepository.ListAsync(status, offset, limit, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Project> UpdateProjectAsync(
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _projectRepository.UpdateAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ArchiveProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new MorphDbException("PROJECT_NOT_FOUND", $"Project with ID '{projectId}' not found.");

        if (project.Status is not ProjectStatus.Active)
        {
            throw new MorphDbException(
                "INVALID_STATUS_TRANSITION",
                $"Cannot archive project in status '{project.Status}'.");
        }

        await _projectRepository.UpdateStatusAsync(projectId, ProjectStatus.Archiving, cancellationToken);

        // TODO: In Phase 23, implement actual archival process (backup, etc.)

        await _projectRepository.UpdateStatusAsync(projectId, ProjectStatus.Archived, cancellationToken);

        LogProjectArchived(_logger, projectId);
    }

    /// <inheritdoc/>
    public async Task DeleteProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new MorphDbException("PROJECT_NOT_FOUND", $"Project with ID '{projectId}' not found.");

        LogDeletingProject(_logger, projectId, project.Name);

        // Update status to Deleting
        await _projectRepository.UpdateStatusAsync(projectId, ProjectStatus.Deleting, cancellationToken);

        try
        {
            // Drop project schemas
            await _schemaLayerService.DropProjectSchemasAsync(projectId, cancellationToken);

            // Soft delete project record
            await _projectRepository.DeleteAsync(projectId, cancellationToken);

            LogProjectDeleted(_logger, projectId);
        }
        catch (Exception ex)
        {
            LogProjectDeletionFailed(_logger, projectId, ex);

            // Revert status on failure
            await _projectRepository.UpdateStatusAsync(projectId, project.Status, cancellationToken);

            throw new MorphDbException(
                "PROJECT_DELETION_FAILED",
                $"Failed to delete project: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<ProjectSchemaStats> GetProjectStatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new MorphDbException("PROJECT_NOT_FOUND", $"Project with ID '{projectId}' not found.");

        return await _schemaLayerService.GetProjectStatsAsync(projectId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SchemaHealthReport> ValidateProjectHealthAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new MorphDbException("PROJECT_NOT_FOUND", $"Project with ID '{projectId}' not found.");

        return await _schemaLayerService.ValidateSchemaHealthAsync(projectId, cancellationToken);
    }

    // LoggerMessage delegates for high-performance logging
    [LoggerMessage(LogLevel.Information, "Creating project: {Name}")]
    private static partial void LogCreatingProject(ILogger logger, string name);

    [LoggerMessage(LogLevel.Information, "Project created successfully: {ProjectId} ({Slug})")]
    private static partial void LogProjectCreated(ILogger logger, Guid projectId, string slug);

    [LoggerMessage(LogLevel.Error, "Failed to provision schemas for project {ProjectId}")]
    private static partial void LogProjectProvisioningFailed(ILogger logger, Guid projectId, Exception exception);

    [LoggerMessage(LogLevel.Information, "Cleaned up failed project record: {ProjectId}")]
    private static partial void LogProjectCleanedUp(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Warning, "Failed to cleanup project record: {ProjectId}")]
    private static partial void LogProjectCleanupFailed(ILogger logger, Guid projectId, Exception exception);

    [LoggerMessage(LogLevel.Information, "Project archived: {ProjectId}")]
    private static partial void LogProjectArchived(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Warning, "Deleting project: {ProjectId} ({Name})")]
    private static partial void LogDeletingProject(ILogger logger, Guid projectId, string name);

    [LoggerMessage(LogLevel.Information, "Project deleted: {ProjectId}")]
    private static partial void LogProjectDeleted(ILogger logger, Guid projectId);

    [LoggerMessage(LogLevel.Error, "Failed to delete project: {ProjectId}")]
    private static partial void LogProjectDeletionFailed(ILogger logger, Guid projectId, Exception exception);
}
