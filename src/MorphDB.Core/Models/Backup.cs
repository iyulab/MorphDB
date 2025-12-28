namespace MorphDB.Core.Models;

/// <summary>
/// Represents a backup of a project's data and/or schema.
/// Backups are created using pg_dump and stored locally or in cloud storage.
/// </summary>
public sealed record Backup
{
    /// <summary>
    /// Unique identifier for the backup.
    /// </summary>
    public Guid BackupId { get; init; }

    /// <summary>
    /// The project this backup belongs to.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Human-readable name for the backup.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description for the backup.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Type of backup (full, schema-only, data-only).
    /// </summary>
    public BackupType Type { get; init; } = BackupType.Full;

    /// <summary>
    /// Current status of the backup.
    /// </summary>
    public BackupStatus Status { get; init; } = BackupStatus.Pending;

    /// <summary>
    /// Size of the backup in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Storage location of the backup file.
    /// For local storage: file path
    /// For cloud storage: URI (s3://, gs://, azure://)
    /// </summary>
    public string? StoragePath { get; init; }

    /// <summary>
    /// Storage backend type.
    /// </summary>
    public BackupStorageType StorageType { get; init; } = BackupStorageType.Local;

    /// <summary>
    /// Compression algorithm used.
    /// </summary>
    public BackupCompression Compression { get; init; } = BackupCompression.Gzip;

    /// <summary>
    /// Checksum of the backup file (SHA-256).
    /// </summary>
    public string? Checksum { get; init; }

    /// <summary>
    /// Error message if backup failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// User ID who initiated the backup (null for scheduled backups).
    /// </summary>
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Timestamp when the backup was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Timestamp when the backup completed (success or failure).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Timestamp when the backup expires and can be deleted.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Metadata about what was backed up.
    /// </summary>
    public BackupMetadata? Metadata { get; init; }
}

/// <summary>
/// Type of backup operation.
/// </summary>
public enum BackupType
{
    /// <summary>
    /// Full backup including schema and data.
    /// </summary>
    Full = 0,

    /// <summary>
    /// Schema-only backup (DDL statements).
    /// </summary>
    SchemaOnly = 1,

    /// <summary>
    /// Data-only backup (INSERT statements or COPY data).
    /// </summary>
    DataOnly = 2
}

/// <summary>
/// Status of a backup operation.
/// </summary>
public enum BackupStatus
{
    /// <summary>
    /// Backup is queued but not started.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Backup is currently in progress.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// Backup completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Backup failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Backup was cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Backup has expired and file was deleted.
    /// </summary>
    Expired = 5
}

/// <summary>
/// Storage backend type for backups.
/// </summary>
public enum BackupStorageType
{
    /// <summary>
    /// Local file system storage.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Amazon S3 storage.
    /// </summary>
    S3 = 1,

    /// <summary>
    /// Google Cloud Storage.
    /// </summary>
    Gcs = 2,

    /// <summary>
    /// Azure Blob Storage.
    /// </summary>
    AzureBlob = 3
}

/// <summary>
/// Compression algorithm for backups.
/// </summary>
public enum BackupCompression
{
    /// <summary>
    /// No compression.
    /// </summary>
    None = 0,

    /// <summary>
    /// Gzip compression (pg_dump -Z).
    /// </summary>
    Gzip = 1,

    /// <summary>
    /// Zstandard compression (pg_dump --compress=zstd).
    /// </summary>
    Zstd = 2
}

/// <summary>
/// Metadata about what was included in the backup.
/// </summary>
public sealed class BackupMetadata
{
    /// <summary>
    /// PostgreSQL version of the source database.
    /// </summary>
    public string? PostgresVersion { get; init; }

    /// <summary>
    /// Number of tables backed up.
    /// </summary>
    public int TableCount { get; init; }

    /// <summary>
    /// Estimated total row count across all tables.
    /// </summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>
    /// Names of schemas included in the backup.
    /// </summary>
    public IReadOnlyList<string>? Schemas { get; init; }

    /// <summary>
    /// Names of tables included in the backup.
    /// </summary>
    public IReadOnlyList<string>? Tables { get; init; }

    /// <summary>
    /// Duration of the backup operation in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Request to create a new backup.
/// </summary>
public sealed class CreateBackupRequest
{
    /// <summary>
    /// The project to back up.
    /// </summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Human-readable name for the backup.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Type of backup to create.
    /// </summary>
    public BackupType Type { get; init; } = BackupType.Full;

    /// <summary>
    /// User ID initiating the backup.
    /// </summary>
    public string? InitiatedBy { get; init; }

    /// <summary>
    /// Optional expiration time for the backup.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Request to restore a backup.
/// </summary>
public sealed class RestoreBackupRequest
{
    /// <summary>
    /// The backup to restore.
    /// </summary>
    public Guid BackupId { get; init; }

    /// <summary>
    /// Target project ID (can be different from original for restoring to new project).
    /// </summary>
    public Guid TargetProjectId { get; init; }

    /// <summary>
    /// Whether to drop existing objects before restore.
    /// </summary>
    public bool DropExisting { get; init; }

    /// <summary>
    /// User ID initiating the restore.
    /// </summary>
    public string? InitiatedBy { get; init; }
}

/// <summary>
/// Result of a restore operation.
/// </summary>
public sealed class RestoreResult
{
    /// <summary>
    /// Whether the restore was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if restore failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of tables restored.
    /// </summary>
    public int TablesRestored { get; init; }

    /// <summary>
    /// Duration of the restore operation in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }
}
