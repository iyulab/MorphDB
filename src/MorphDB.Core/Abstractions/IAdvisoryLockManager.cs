namespace MorphDB.Core.Abstractions;

/// <summary>
/// Manages PostgreSQL advisory locks for DDL serialization.
/// </summary>
public interface IAdvisoryLockManager
{
    /// <summary>
    /// Acquires a transaction-level advisory lock for DDL operations.
    /// The lock is automatically released when the transaction commits or rolls back.
    /// </summary>
    Task<IAsyncDisposable> AcquireDdlLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to acquire a DDL lock without waiting.
    /// </summary>
    Task<(bool Acquired, IAsyncDisposable? Lock)> TryAcquireDdlLockAsync(
        string resourceIdentifier,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a session-level advisory lock.
    /// Must be explicitly released.
    /// </summary>
    Task<IAsyncDisposable> AcquireSessionLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for advisory lock behavior.
/// </summary>
public sealed class AdvisoryLockOptions
{
    /// <summary>
    /// Default timeout for acquiring locks.
    /// </summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retry interval when lock is not immediately available.
    /// </summary>
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetries { get; init; } = 50;
}
