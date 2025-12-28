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
    /// Optional organization ID for hierarchical multi-tenancy.
    /// </summary>
    public Guid? OrganizationId { get; init; }

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
    /// Maximum number of tables allowed in this project.
    /// </summary>
    public int? MaxTables { get; init; }

    /// <summary>
    /// Maximum storage size in bytes for this project.
    /// </summary>
    public long? MaxStorageBytes { get; init; }

    /// <summary>
    /// Rate limiting configuration.
    /// </summary>
    public RateLimitSettings? RateLimits { get; init; }

    /// <summary>
    /// Custom metadata/tags for the project.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Rate limiting settings for a project.
/// </summary>
public sealed class RateLimitSettings
{
    /// <summary>
    /// Maximum requests per minute.
    /// </summary>
    public int? RequestsPerMinute { get; init; }

    /// <summary>
    /// Maximum requests per hour.
    /// </summary>
    public int? RequestsPerHour { get; init; }

    /// <summary>
    /// Maximum concurrent connections.
    /// </summary>
    public int? MaxConcurrentConnections { get; init; }
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

    /// <summary>
    /// Project is suspended (temporarily disabled).
    /// </summary>
    Suspended = 2,

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
