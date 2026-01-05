using MorphDB.Core.Models;

namespace MorphDB.Core.Abstractions;

/// <summary>
/// Service for creating and managing project backups.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new backup for the specified project.
    /// </summary>
    /// <param name="request">The backup creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created backup with initial status.</returns>
    Task<Backup> CreateBackupAsync(CreateBackupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a backup by its ID.
    /// </summary>
    /// <param name="backupId">The backup ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The backup, or null if not found.</returns>
    Task<Backup?> GetBackupAsync(Guid backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all backups for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of backups ordered by creation date descending.</returns>
    Task<IReadOnlyList<Backup>> ListBackupsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a backup to a project.
    /// </summary>
    /// <param name="request">The restore request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the restore operation.</returns>
    Task<RestoreResult> RestoreBackupAsync(RestoreBackupRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup and its associated file.
    /// </summary>
    /// <param name="backupId">The backup ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the backup was deleted, false if not found.</returns>
    Task<bool> DeleteBackupAsync(Guid backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a backup file.
    /// </summary>
    /// <param name="backupId">The backup ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream of the backup file, or null if not found.</returns>
    Task<Stream?> DownloadBackupAsync(Guid backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies backup integrity by checking file existence and checksum.
    /// </summary>
    /// <param name="backupId">The backup ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result with details.</returns>
    Task<BackupVerificationResult> VerifyBackupAsync(Guid backupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of backup verification.
/// </summary>
public sealed record BackupVerificationResult
{
    /// <summary>
    /// Whether the backup passed all verification checks.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Whether the backup file exists.
    /// </summary>
    public bool FileExists { get; init; }

    /// <summary>
    /// Whether the checksum matches the stored value.
    /// </summary>
    public bool ChecksumValid { get; init; }

    /// <summary>
    /// Whether the backup file can be decompressed.
    /// </summary>
    public bool CanDecompress { get; init; }

    /// <summary>
    /// Current file size in bytes.
    /// </summary>
    public long? CurrentSizeBytes { get; init; }

    /// <summary>
    /// Stored file size in bytes (from backup record).
    /// </summary>
    public long? StoredSizeBytes { get; init; }

    /// <summary>
    /// Error message if verification failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Time taken for verification in milliseconds.
    /// </summary>
    public long VerificationDurationMs { get; init; }
}

/// <summary>
/// Repository for backup metadata persistence.
/// </summary>
public interface IBackupRepository
{
    /// <summary>
    /// Creates a new backup record.
    /// </summary>
    Task<Backup> CreateAsync(Backup backup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a backup by ID.
    /// </summary>
    Task<Backup?> GetByIdAsync(Guid backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists backups for a project.
    /// </summary>
    Task<IReadOnlyList<Backup>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a backup record.
    /// </summary>
    Task<Backup> UpdateAsync(Backup backup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup record.
    /// </summary>
    Task<bool> DeleteAsync(Guid backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists expired backups that should be cleaned up.
    /// </summary>
    Task<IReadOnlyList<Backup>> ListExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for backup service configuration.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    /// Local directory path for storing backups.
    /// Default: ./backups
    /// </summary>
    public string LocalStoragePath { get; set; } = "./backups";

    /// <summary>
    /// Path to pg_dump executable.
    /// If null, uses system PATH.
    /// </summary>
    public string? PgDumpPath { get; set; }

    /// <summary>
    /// Path to pg_restore executable.
    /// If null, uses system PATH.
    /// </summary>
    public string? PgRestorePath { get; set; }

    /// <summary>
    /// Default compression level (0-9, 0 = no compression).
    /// </summary>
    public int CompressionLevel { get; set; } = 6;

    /// <summary>
    /// Default expiration days for backups (0 = never expire).
    /// </summary>
    public int DefaultExpirationDays { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent backup operations.
    /// </summary>
    public int MaxConcurrentBackups { get; set; } = 2;

    /// <summary>
    /// Timeout for backup operations in seconds.
    /// </summary>
    public int BackupTimeoutSeconds { get; set; } = 3600;
}
