namespace MorphDB.Core.Encryption;

/// <summary>
/// Service for managing encryption key rotation operations.
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Gets the current key version.
    /// </summary>
    int CurrentKeyVersion { get; }

    /// <summary>
    /// Gets all available key versions.
    /// </summary>
    IReadOnlyList<int> AvailableKeyVersions { get; }

    /// <summary>
    /// Initiates a key rotation for a specific project and table.
    /// Re-encrypts all data with the new key version.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name to rotate keys for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Key rotation result with statistics.</returns>
    Task<KeyRotationResult> RotateTableKeyAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a key rotation for all tables of a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Key rotation result with statistics.</returns>
    Task<KeyRotationResult> RotateProjectKeysAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current key rotation status for a table.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Key rotation status.</returns>
    Task<KeyRotationStatus> GetRotationStatusAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all data in a table is encrypted with the current key version.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    Task<KeyValidationResult> ValidateEncryptionAsync(
        Guid projectId,
        string tableName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a key rotation operation.
/// </summary>
public sealed record KeyRotationResult
{
    /// <summary>
    /// Whether the rotation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The table that was rotated.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The previous key version.
    /// </summary>
    public required int PreviousKeyVersion { get; init; }

    /// <summary>
    /// The new key version.
    /// </summary>
    public required int NewKeyVersion { get; init; }

    /// <summary>
    /// Number of rows processed.
    /// </summary>
    public required long RowsProcessed { get; init; }

    /// <summary>
    /// Number of columns re-encrypted.
    /// </summary>
    public required int ColumnsRotated { get; init; }

    /// <summary>
    /// Time taken for the rotation.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Error message if the rotation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When the rotation started.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// When the rotation completed.
    /// </summary>
    public required DateTimeOffset CompletedAt { get; init; }
}

/// <summary>
/// Status of an ongoing or completed key rotation.
/// </summary>
public sealed record KeyRotationStatus
{
    /// <summary>
    /// The current state of the rotation.
    /// </summary>
    public required KeyRotationState State { get; init; }

    /// <summary>
    /// The table being rotated.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The current key version used in the table.
    /// </summary>
    public required int CurrentKeyVersion { get; init; }

    /// <summary>
    /// The target key version (if rotation is in progress).
    /// </summary>
    public int? TargetKeyVersion { get; init; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; init; }

    /// <summary>
    /// Number of rows processed so far.
    /// </summary>
    public long RowsProcessed { get; init; }

    /// <summary>
    /// Total number of rows to process.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }

    /// <summary>
    /// When the rotation started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Last rotation completion time.
    /// </summary>
    public DateTimeOffset? LastRotatedAt { get; init; }
}

/// <summary>
/// State of a key rotation operation.
/// </summary>
public enum KeyRotationState
{
    /// <summary>
    /// No rotation in progress, table is up to date.
    /// </summary>
    Idle,

    /// <summary>
    /// Rotation is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Rotation is pending (scheduled but not started).
    /// </summary>
    Pending,

    /// <summary>
    /// Rotation completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Rotation failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Rotation was cancelled.
    /// </summary>
    Cancelled
}

/// <summary>
/// Result of encryption validation.
/// </summary>
public sealed record KeyValidationResult
{
    /// <summary>
    /// Whether all data is encrypted with the current key version.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// The table that was validated.
    /// </summary>
    public required string TableName { get; init; }

    /// <summary>
    /// The expected key version.
    /// </summary>
    public required int ExpectedKeyVersion { get; init; }

    /// <summary>
    /// Total number of encrypted values checked.
    /// </summary>
    public required long TotalEncryptedValues { get; init; }

    /// <summary>
    /// Number of values encrypted with the current key version.
    /// </summary>
    public required long CurrentVersionCount { get; init; }

    /// <summary>
    /// Number of values encrypted with older key versions.
    /// </summary>
    public required long OldVersionCount { get; init; }

    /// <summary>
    /// Breakdown of counts by key version.
    /// </summary>
    public required IReadOnlyDictionary<int, long> VersionBreakdown { get; init; }

    /// <summary>
    /// Number of unencrypted values found.
    /// </summary>
    public required long UnencryptedCount { get; init; }
}
