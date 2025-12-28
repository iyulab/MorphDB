using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using IOPath = System.IO.Path;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Controller for backup and restore operations.
/// </summary>
[ApiController]
[Route("api/projects/{projectId:guid}/backups")]
[Authorize]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly IProjectService _projectService;
    private readonly IPermissionService _permissionService;

    public BackupController(
        IBackupService backupService,
        IProjectService projectService,
        IPermissionService permissionService)
    {
        _backupService = backupService;
        _projectService = projectService;
        _permissionService = permissionService;
    }

    /// <summary>
    /// Lists all backups for a project.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BackupResponse>>> ListBackups(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var backups = await _backupService.ListBackupsAsync(projectId, cancellationToken);
        return Ok(backups.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Gets a backup by ID.
    /// </summary>
    [HttpGet("{backupId:guid}")]
    public async Task<ActionResult<BackupResponse>> GetBackup(
        Guid projectId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
        if (backup is null || backup.ProjectId != projectId)
        {
            return NotFound();
        }

        return Ok(MapToResponse(backup));
    }

    /// <summary>
    /// Creates a new backup.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BackupResponse>> CreateBackup(
        Guid projectId,
        [FromBody] CreateBackupRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var project = await _projectService.GetProjectAsync(projectId, cancellationToken);
        if (project is null)
        {
            return NotFound(new { error = "Project not found" });
        }

        var createRequest = new CreateBackupRequest
        {
            ProjectId = projectId,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            InitiatedBy = userId,
            ExpiresAt = request.ExpiresInDays.HasValue
                ? DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays.Value)
                : null
        };

        var backup = await _backupService.CreateBackupAsync(createRequest, cancellationToken);
        return CreatedAtAction(nameof(GetBackup), new { projectId, backupId = backup.BackupId }, MapToResponse(backup));
    }

    /// <summary>
    /// Restores a backup to a project.
    /// </summary>
    [HttpPost("{backupId:guid}/restore")]
    public async Task<ActionResult<RestoreResultResponse>> RestoreBackup(
        Guid projectId,
        Guid backupId,
        [FromBody] RestoreBackupRequestDto? request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Check permission on source project
        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
        if (backup is null || backup.ProjectId != projectId)
        {
            return NotFound();
        }

        var targetProjectId = request?.TargetProjectId ?? projectId;

        // If restoring to different project, check permission on target
        if (targetProjectId != projectId)
        {
            if (!await _permissionService.HasProjectPermissionAsync(userId, targetProjectId, Permissions.Project.ManageBackups, cancellationToken))
            {
                return Forbid();
            }
        }

        var restoreRequest = new RestoreBackupRequest
        {
            BackupId = backupId,
            TargetProjectId = targetProjectId,
            DropExisting = request?.DropExisting ?? false,
            InitiatedBy = userId
        };

        var result = await _backupService.RestoreBackupAsync(restoreRequest, cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new RestoreResultResponse
        {
            Success = result.Success,
            TablesRestored = result.TablesRestored,
            DurationMs = result.DurationMs
        });
    }

    /// <summary>
    /// Downloads a backup file.
    /// </summary>
    [HttpGet("{backupId:guid}/download")]
    public async Task<IActionResult> DownloadBackup(
        Guid projectId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
        if (backup is null || backup.ProjectId != projectId)
        {
            return NotFound();
        }

        var stream = await _backupService.DownloadBackupAsync(backupId, cancellationToken);
        if (stream is null)
        {
            return NotFound(new { error = "Backup file not found" });
        }

        var fileName = IOPath.GetFileName(backup.StoragePath) ?? $"backup_{backupId}.sql.gz";
        return File(stream, "application/gzip", fileName);
    }

    /// <summary>
    /// Deletes a backup.
    /// </summary>
    [HttpDelete("{backupId:guid}")]
    public async Task<IActionResult> DeleteBackup(
        Guid projectId,
        Guid backupId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await _permissionService.HasProjectPermissionAsync(userId, projectId, Permissions.Project.ManageBackups, cancellationToken))
        {
            return Forbid();
        }

        var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
        if (backup is null || backup.ProjectId != projectId)
        {
            return NotFound();
        }

        await _backupService.DeleteBackupAsync(backupId, cancellationToken);
        return NoContent();
    }

    private string? GetUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }

    private static BackupResponse MapToResponse(Backup backup)
    {
        return new BackupResponse
        {
            BackupId = backup.BackupId,
            ProjectId = backup.ProjectId,
            Name = backup.Name,
            Description = backup.Description,
            Type = backup.Type,
            Status = backup.Status,
            SizeBytes = backup.SizeBytes,
            StorageType = backup.StorageType,
            Compression = backup.Compression,
            Checksum = backup.Checksum,
            ErrorMessage = backup.ErrorMessage,
            InitiatedBy = backup.InitiatedBy,
            StartedAt = backup.StartedAt,
            CompletedAt = backup.CompletedAt,
            ExpiresAt = backup.ExpiresAt,
            Metadata = backup.Metadata
        };
    }
}

#region DTOs

/// <summary>
/// Response DTO for backup.
/// </summary>
public sealed class BackupResponse
{
    public Guid BackupId { get; init; }
    public Guid ProjectId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public BackupType Type { get; init; }
    public BackupStatus Status { get; init; }
    public long SizeBytes { get; init; }
    public BackupStorageType StorageType { get; init; }
    public BackupCompression Compression { get; init; }
    public string? Checksum { get; init; }
    public string? ErrorMessage { get; init; }
    public string? InitiatedBy { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public BackupMetadata? Metadata { get; init; }
}

/// <summary>
/// Request DTO for creating a backup.
/// </summary>
public sealed class CreateBackupRequestDto
{
    /// <summary>
    /// Human-readable name for the backup.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Type of backup (Full, SchemaOnly, DataOnly).
    /// </summary>
    public BackupType Type { get; init; } = BackupType.Full;

    /// <summary>
    /// Number of days until the backup expires (null for no expiration).
    /// </summary>
    public int? ExpiresInDays { get; init; }
}

/// <summary>
/// Request DTO for restoring a backup.
/// </summary>
public sealed class RestoreBackupRequestDto
{
    /// <summary>
    /// Target project ID (defaults to source project if not specified).
    /// </summary>
    public Guid? TargetProjectId { get; init; }

    /// <summary>
    /// Whether to drop existing objects before restore.
    /// </summary>
    public bool DropExisting { get; init; }
}

/// <summary>
/// Response DTO for restore result.
/// </summary>
public sealed class RestoreResultResponse
{
    public bool Success { get; init; }
    public int TablesRestored { get; init; }
    public long DurationMs { get; init; }
}

#endregion
