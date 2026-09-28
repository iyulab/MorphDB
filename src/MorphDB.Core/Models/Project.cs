namespace MorphDB.Core.Models;

/// <summary>
/// Represents a project with isolated PostgreSQL schemas for system and data layers.
/// Each project has two schemas, named when it is created and recorded here:
/// - System schema (p_{id}_sys): Contains metadata tables (_tables, _columns, etc.)
/// - Data schema (p_{id}_dat): Contains user-defined data tables
/// A new project's {id} is its whole id as 32 hex digits; a project created before that rule keeps
/// the first-eight-digit names it was given.
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
    /// PostgreSQL schema name for system/metadata tables, as recorded when the project was created.
    /// Format: p_{projectIdAs32HexDigits}_sys (earlier projects: p_{first8HexDigits}_sys)
    /// </summary>
    public required string SystemSchema { get; init; }

    /// <summary>
    /// PostgreSQL schema name for user data tables, as recorded when the project was created.
    /// Format: p_{projectIdAs32HexDigits}_dat (earlier projects: p_{first8HexDigits}_dat)
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
    /// How many days of audit history the project keeps. Entries older than this are removed by
    /// the database itself — a store that grows without bound would otherwise make its own upkeep
    /// the caller's problem, and the caller has no way to reach the table.
    /// <para>
    /// Null, the default, keeps everything: a project that has not asked for a retention window
    /// must not silently start losing history when this setting is introduced.
    /// </para>
    /// </summary>
    public int? AuditLogRetentionDays { get; init; }

    /// <summary>
    /// Whether relations created without saying so are checked on write. True, the default, keeps
    /// enforcement as the project's standing answer; a project that rebuilds its tables wholesale
    /// sets it false once instead of repeating that declaration on every relation.
    /// <para>
    /// A relation that states its own <c>EnforceOnWrite</c> overrides this. The value is resolved
    /// when the relation is created and stored on it, so changing this later leaves existing
    /// relations as they were — a stored relation must not start or stop claiming enforcement that
    /// its physical constraints no longer match.
    /// </para>
    /// </summary>
    public bool DefaultEnforceOnWrite { get; init; } = true;

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
