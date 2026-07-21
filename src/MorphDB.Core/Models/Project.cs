namespace MorphDB.Core.Models;

/// <summary>
/// Represents a project with isolated PostgreSQL schemas for system and data layers.
/// Each project has two schemas:
/// - System schema (p_{id8}_sys): Contains metadata tables (_tables, _columns, etc.)
/// - Data schema (p_{id8}_dat): Contains user-defined data tables
/// </summary>
public sealed class Project
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Human-readable project name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL-safe unique identifier (e.g., "my-project").
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// PostgreSQL schema name for system/metadata tables.
    /// Format: p_{first8charsOfProjectId}_sys
    /// </summary>
    public required string SystemSchema { get; init; }

    /// <summary>
    /// PostgreSQL schema name for user data tables.
    /// Format: p_{first8charsOfProjectId}_dat
    /// </summary>
    public required string DataSchema { get; init; }

    /// <summary>
    /// Project configuration and settings stored as JSON.
    /// </summary>
    public ProjectSettings? Settings { get; init; }

    /// <summary>
    /// Project status.
    /// </summary>
    public ProjectStatus Status { get; init; } = ProjectStatus.Active;

    /// <summary>
    /// Timestamp when the project was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when the project was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Project-specific settings and configuration.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>
    /// Default locale for the project.
    /// </summary>
    public string? DefaultLocale { get; init; }

    /// <summary>
    /// Timezone for the project (e.g., "Asia/Seoul").
    /// </summary>
    public string? Timezone { get; init; }

    /// <summary>
    /// Whether to enable audit logging for this project.
    /// </summary>
    public bool EnableAuditLog { get; init; } = true;

    /// <summary>
    /// Custom metadata/tags for the project.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Project lifecycle status.
/// </summary>
public enum ProjectStatus
{
    /// <summary>
    /// Project is being provisioned (schemas being created).
    /// </summary>
    Provisioning = 0,

    /// <summary>
    /// Project is active and operational.
    /// </summary>
    Active = 1,

    // 2 was Suspended, a subscription state this layer has no business holding. It also did nothing:
    // requests resolve a schema name from the project id by formatting it, without ever reading the
    // project row, so a suspended project kept serving reads and writes. The number is left unused
    // rather than reassigned, because rows written before the removal still carry it.

    /// <summary>
    /// Project is being archived.
    /// </summary>
    Archiving = 3,

    /// <summary>
    /// Project is archived (read-only).
    /// </summary>
    Archived = 4,

    /// <summary>
    /// Project is marked for deletion.
    /// </summary>
    Deleting = 5,

    /// <summary>
    /// Project has been deleted (soft delete).
    /// </summary>
    Deleted = 6
}
