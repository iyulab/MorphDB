namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for migrating legacy databases to the new project-based schema structure.
/// Handles migration from existing public/shared schema to project-specific schemas.
/// </summary>
public interface ILegacyMigrationService
{
    /// <summary>
    /// Analyzes an existing legacy schema and returns a migration plan.
    /// </summary>
    /// <param name="legacySchema">The source schema name (typically "public").</param>
    /// <param name="targetProjectId">The target project ID for the migration.</param>
    /// <param name="options">Migration options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A migration plan with estimated work and potential issues.</returns>
    Task<LegacyMigrationPlan> AnalyzeLegacySchemaAsync(
        string legacySchema,
        Guid targetProjectId,
        LegacyMigrationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a legacy migration plan.
    /// </summary>
    /// <param name="plan">The migration plan to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the migration operation.</returns>
    Task<LegacyMigrationResult> ExecuteMigrationAsync(
        LegacyMigrationPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a legacy migration completed successfully.
    /// </summary>
    /// <param name="legacySchema">The source schema that was migrated.</param>
    /// <param name="projectId">The target project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any discrepancies found.</returns>
    Task<LegacyMigrationValidationResult> ValidateMigrationAsync(
        string legacySchema,
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back a legacy migration if possible.
    /// </summary>
    /// <param name="projectId">The project ID to rollback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the rollback operation.</returns>
    Task<LegacyMigrationResult> RollbackMigrationAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an ongoing or completed legacy migration.
    /// </summary>
    /// <param name="migrationId">The migration ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current status of the migration.</returns>
    Task<LegacyMigrationStatus?> GetMigrationStatusAsync(
        Guid migrationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all legacy migrations for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of migration records.</returns>
    Task<IReadOnlyList<LegacyMigrationStatus>> GetMigrationHistoryAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for legacy migration.
/// </summary>
public sealed class LegacyMigrationOptions
{
    /// <summary>
    /// If true, creates a backup of the legacy schema before migration.
    /// </summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>
    /// If true, validates data integrity after each table migration.
    /// </summary>
    public bool ValidateAfterEachTable { get; init; } = true;

    /// <summary>
    /// If true, drops the legacy tables after successful migration.
    /// </summary>
    public bool DropLegacyTablesOnSuccess { get; init; }

    /// <summary>
    /// Tables to exclude from migration (by name pattern).
    /// </summary>
    public IReadOnlyList<string>? ExcludeTablePatterns { get; init; }

    /// <summary>
    /// Tables to include in migration (by name pattern). If empty, all tables are included.
    /// </summary>
    public IReadOnlyList<string>? IncludeTablePatterns { get; init; }

    /// <summary>
    /// Batch size for data migration.
    /// </summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Maximum number of parallel table migrations.
    /// </summary>
    public int MaxParallelism { get; init; } = 4;
}

/// <summary>
/// A plan for migrating a legacy schema to project-based schemas.
/// </summary>
public sealed class LegacyMigrationPlan
{
    /// <summary>
    /// Unique identifier for this migration plan.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The source legacy schema name.
    /// </summary>
    public required string LegacySchema { get; init; }

    /// <summary>
    /// The target project ID.
    /// </summary>
    public Guid TargetProjectId { get; init; }

    /// <summary>
    /// The target system schema name.
    /// </summary>
    public required string TargetSystemSchema { get; init; }

    /// <summary>
    /// The target data schema name.
    /// </summary>
    public required string TargetDataSchema { get; init; }

    /// <summary>
    /// Tables to be migrated.
    /// </summary>
    public required IReadOnlyList<LegacyTableMigrationPlan> Tables { get; init; }

    /// <summary>
    /// Estimated total rows to migrate.
    /// </summary>
    public long EstimatedTotalRows { get; init; }

    /// <summary>
    /// Estimated migration duration in seconds.
    /// </summary>
    public int EstimatedDurationSeconds { get; init; }

    /// <summary>
    /// Potential issues detected during analysis.
    /// </summary>
    public required IReadOnlyList<LegacyMigrationIssue> Issues { get; init; }

    /// <summary>
    /// Whether the migration plan can be executed.
    /// </summary>
    public bool CanExecute { get; init; }

    /// <summary>
    /// Options that will be used for migration.
    /// </summary>
    public required LegacyMigrationOptions Options { get; init; }

    /// <summary>
    /// Timestamp when the plan was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Plan for migrating a single legacy table.
/// </summary>
public sealed class LegacyTableMigrationPlan
{
    /// <summary>
    /// The source table name in the legacy schema.
    /// </summary>
    public required string SourceTableName { get; init; }

    /// <summary>
    /// The target table identifier in the data schema.
    /// </summary>
    public required string TargetTableId { get; init; }

    /// <summary>
    /// Column mappings from source to target.
    /// </summary>
    public required IReadOnlyList<LegacyColumnMapping> ColumnMappings { get; init; }

    /// <summary>
    /// Estimated row count.
    /// </summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>
    /// Estimated size in bytes.
    /// </summary>
    public long EstimatedSizeBytes { get; init; }

    /// <summary>
    /// Whether this table has system metadata (will be migrated to system schema).
    /// </summary>
    public bool IsSystemTable { get; init; }

    /// <summary>
    /// Order in which this table should be migrated (for foreign key dependencies).
    /// </summary>
    public int MigrationOrder { get; init; }
}

/// <summary>
/// Mapping of a legacy column to the new schema.
/// </summary>
public sealed class LegacyColumnMapping
{
    /// <summary>
    /// The source column name.
    /// </summary>
    public required string SourceColumnName { get; init; }

    /// <summary>
    /// The target column identifier.
    /// </summary>
    public required string TargetColumnId { get; init; }

    /// <summary>
    /// The source data type.
    /// </summary>
    public required string SourceDataType { get; init; }

    /// <summary>
    /// The target MorphDB data type.
    /// </summary>
    public required string TargetDataType { get; init; }

    /// <summary>
    /// Whether type conversion is required.
    /// </summary>
    public bool RequiresConversion { get; init; }

    /// <summary>
    /// Conversion expression if required.
    /// </summary>
    public string? ConversionExpression { get; init; }
}

/// <summary>
/// An issue detected during legacy migration analysis.
/// </summary>
public sealed class LegacyMigrationIssue
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
    public LegacyMigrationIssueSeverity Severity { get; init; }

    /// <summary>
    /// The table affected by this issue, if applicable.
    /// </summary>
    public string? AffectedTable { get; init; }

    /// <summary>
    /// The column affected by this issue, if applicable.
    /// </summary>
    public string? AffectedColumn { get; init; }

    /// <summary>
    /// Suggested resolution for the issue.
    /// </summary>
    public string? SuggestedResolution { get; init; }
}

/// <summary>
/// Severity of a legacy migration issue.
/// </summary>
public enum LegacyMigrationIssueSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning that should be reviewed but won't block migration.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error that will prevent migration.
    /// </summary>
    Error = 2
}

/// <summary>
/// Result of a legacy migration operation.
/// </summary>
public sealed class LegacyMigrationResult
{
    /// <summary>
    /// The migration plan ID.
    /// </summary>
    public Guid MigrationId { get; init; }

    /// <summary>
    /// Whether the migration was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Tables that were successfully migrated.
    /// </summary>
    public required IReadOnlyList<LegacyTableMigrationResult> MigratedTables { get; init; }

    /// <summary>
    /// Total rows migrated.
    /// </summary>
    public long TotalRowsMigrated { get; init; }

    /// <summary>
    /// Total duration in milliseconds.
    /// </summary>
    public long TotalDurationMs { get; init; }

    /// <summary>
    /// Error message if migration failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The table where migration failed, if applicable.
    /// </summary>
    public string? FailedAtTable { get; init; }

    /// <summary>
    /// Whether a backup was created.
    /// </summary>
    public bool BackupCreated { get; init; }

    /// <summary>
    /// Backup identifier if a backup was created.
    /// </summary>
    public string? BackupIdentifier { get; init; }
}

/// <summary>
/// Result of migrating a single table.
/// </summary>
public sealed class LegacyTableMigrationResult
{
    /// <summary>
    /// The source table name.
    /// </summary>
    public required string SourceTableName { get; init; }

    /// <summary>
    /// The target table identifier.
    /// </summary>
    public required string TargetTableId { get; init; }

    /// <summary>
    /// Whether the table migration was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Rows migrated for this table.
    /// </summary>
    public long RowsMigrated { get; init; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Error message if this table migration failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Validation result for a legacy migration.
/// </summary>
public sealed class LegacyMigrationValidationResult
{
    /// <summary>
    /// Whether the migration was valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Table validation results.
    /// </summary>
    public required IReadOnlyList<LegacyTableValidationResult> TableResults { get; init; }

    /// <summary>
    /// Total source rows.
    /// </summary>
    public long TotalSourceRows { get; init; }

    /// <summary>
    /// Total target rows.
    /// </summary>
    public long TotalTargetRows { get; init; }

    /// <summary>
    /// Discrepancy count.
    /// </summary>
    public long DiscrepancyCount { get; init; }

    /// <summary>
    /// Validation issues found.
    /// </summary>
    public required IReadOnlyList<LegacyMigrationIssue> Issues { get; init; }
}

/// <summary>
/// Validation result for a single table.
/// </summary>
public sealed class LegacyTableValidationResult
{
    /// <summary>
    /// The table name.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// Source row count.
    /// </summary>
    public long SourceRowCount { get; init; }

    /// <summary>
    /// Target row count.
    /// </summary>
    public long TargetRowCount { get; init; }

    /// <summary>
    /// Whether counts match.
    /// </summary>
    public bool CountsMatch { get; init; }

    /// <summary>
    /// Whether a checksum validation passed (if performed).
    /// </summary>
    public bool? ChecksumValid { get; init; }
}

/// <summary>
/// Status of a legacy migration.
/// </summary>
public sealed class LegacyMigrationStatus
{
    /// <summary>
    /// The migration ID.
    /// </summary>
    public Guid MigrationId { get; init; }

    /// <summary>
    /// The project ID.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// The source legacy schema.
    /// </summary>
    public required string LegacySchema { get; init; }

    /// <summary>
    /// Current state of the migration.
    /// </summary>
    public LegacyMigrationState State { get; init; }

    /// <summary>
    /// Current progress percentage (0-100).
    /// </summary>
    public int ProgressPercent { get; init; }

    /// <summary>
    /// Current table being migrated.
    /// </summary>
    public string? CurrentTable { get; init; }

    /// <summary>
    /// Tables completed.
    /// </summary>
    public int TablesCompleted { get; init; }

    /// <summary>
    /// Total tables to migrate.
    /// </summary>
    public int TotalTables { get; init; }

    /// <summary>
    /// Rows migrated so far.
    /// </summary>
    public long RowsMigrated { get; init; }

    /// <summary>
    /// Total estimated rows.
    /// </summary>
    public long TotalEstimatedRows { get; init; }

    /// <summary>
    /// When the migration started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the migration completed, if finished.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// State of a legacy migration.
/// </summary>
public enum LegacyMigrationState
{
    /// <summary>
    /// Migration is pending/queued.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Migration is currently running.
    /// </summary>
    Running = 1,

    /// <summary>
    /// Migration completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Migration failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Migration was rolled back.
    /// </summary>
    RolledBack = 4,

    /// <summary>
    /// Migration is being validated.
    /// </summary>
    Validating = 5
}
