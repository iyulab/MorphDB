namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for managing database schema migrations within a project.
/// Handles versioned schema changes with locking and rollback support.
/// </summary>
public interface ISchemaMigrationService
{
    /// <summary>
    /// Gets the current migration version for a project's system schema.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current migration version, or 0 if no migrations have been applied.</returns>
    Task<int> GetCurrentVersionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of all applied migrations for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of applied migrations in order.</returns>
    Task<IReadOnlyList<MigrationRecord>> GetAppliedMigrationsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the list of pending migrations that have not been applied.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of pending migrations.</returns>
    Task<IReadOnlyList<MigrationInfo>> GetPendingMigrationsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies all pending migrations to a project's schema.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the migration operation.</returns>
    Task<MigrationResult> MigrateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies migrations up to a specific version.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="targetVersion">The target version to migrate to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the migration operation.</returns>
    Task<MigrationResult> MigrateToVersionAsync(
        Guid projectId,
        int targetVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the last applied migration.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the rollback operation.</returns>
    Task<MigrationResult> RollbackAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back migrations to a specific version.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="targetVersion">The target version to rollback to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the rollback operation.</returns>
    Task<MigrationResult> RollbackToVersionAsync(
        Guid projectId,
        int targetVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a migration can be applied without errors.
    /// Does not actually apply the migration.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any potential issues.</returns>
    Task<MigrationValidationResult> ValidateMigrationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a migration.
/// </summary>
public sealed class MigrationInfo
{
    /// <summary>
    /// Version number of the migration.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Name/description of the migration.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the migration does.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this migration is reversible.
    /// </summary>
    public bool IsReversible { get; init; } = true;

    /// <summary>
    /// Timestamp when this migration was created/defined.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Record of an applied migration.
/// </summary>
public sealed class MigrationRecord
{
    /// <summary>
    /// Unique identifier for this migration record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Version number of the migration.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Name/description of the migration.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When this migration was applied.
    /// </summary>
    public DateTimeOffset AppliedAt { get; init; }

    /// <summary>
    /// Duration of the migration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Checksum of the migration script for integrity validation.
    /// </summary>
    public string? Checksum { get; init; }

    /// <summary>
    /// Whether this migration was rolled back.
    /// </summary>
    public bool IsRolledBack { get; init; }

    /// <summary>
    /// When this migration was rolled back, if applicable.
    /// </summary>
    public DateTimeOffset? RolledBackAt { get; init; }
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// Whether the migration was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Starting version before migration.
    /// </summary>
    public int FromVersion { get; init; }

    /// <summary>
    /// Target version after migration.
    /// </summary>
    public int ToVersion { get; init; }

    /// <summary>
    /// List of migrations that were applied.
    /// </summary>
    public required IReadOnlyList<MigrationStepResult> Steps { get; init; }

    /// <summary>
    /// Total duration of the migration in milliseconds.
    /// </summary>
    public long TotalDurationMs { get; init; }

    /// <summary>
    /// Error message if migration failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The migration step that failed, if any.
    /// </summary>
    public int? FailedAtVersion { get; init; }
}

/// <summary>
/// Result of a single migration step.
/// </summary>
public sealed class MigrationStepResult
{
    /// <summary>
    /// Version of this migration step.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Name of the migration.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether this step was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Duration of this step in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Error message if this step failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of migration validation.
/// </summary>
public sealed class MigrationValidationResult
{
    /// <summary>
    /// Whether the migration can be applied.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Current schema version.
    /// </summary>
    public int CurrentVersion { get; init; }

    /// <summary>
    /// Target version after migration.
    /// </summary>
    public int TargetVersion { get; init; }

    /// <summary>
    /// Number of migrations to be applied.
    /// </summary>
    public int PendingMigrationCount { get; init; }

    /// <summary>
    /// List of validation issues, if any.
    /// </summary>
    public required IReadOnlyList<MigrationValidationIssue> Issues { get; init; }

    /// <summary>
    /// Estimated duration in milliseconds.
    /// </summary>
    public long? EstimatedDurationMs { get; init; }
}

/// <summary>
/// A validation issue found during migration validation.
/// </summary>
public sealed class MigrationValidationIssue
{
    /// <summary>
    /// Error code for the issue.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Description of the issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public MigrationIssueSeverity Severity { get; init; }

    /// <summary>
    /// The migration version affected by this issue.
    /// </summary>
    public int? AffectedVersion { get; init; }
}

/// <summary>
/// Severity of a migration validation issue.
/// </summary>
public enum MigrationIssueSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning that should be reviewed.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error that will prevent migration.
    /// </summary>
    Error = 2
}
