using System.Security.Cryptography;
using System.Text;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// PostgreSQL advisory lock manager for DDL serialization.
/// </summary>
/// <remarks>
/// Every lock is a session-level advisory lock held by a connection of its own, for as long as the
/// handle is not disposed: the schema change it guards runs on other connections, in transactions of
/// their own, so a lock bound to a transaction on the lock's connection would protect nothing. A
/// transaction-level lock taken outside a transaction is released by the very statement that takes
/// it. Disposing the handle unlocks and returns the connection; if the unlock never runs, the pool's
/// reset on return releases every advisory lock the session held.
/// </remarks>
public sealed class PostgresAdvisoryLockManager : IAdvisoryLockManager
{
    private readonly MorphConnectionFactory _connections;
    private readonly AdvisoryLockOptions _options;

    public PostgresAdvisoryLockManager(MorphConnectionFactory connections, AdvisoryLockOptions? options = null)
    {
        _connections = connections;
        _options = options ?? new AdvisoryLockOptions();
    }

    public Task<IAsyncDisposable> AcquireDdlLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AcquireAsync(resourceIdentifier, timeout, cancellationToken);

    public async Task<(bool Acquired, IAsyncDisposable? Lock)> TryAcquireDdlLockAsync(
        string resourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        var lockKey = ComputeLockKey(resourceIdentifier);
        var connection = await _connections.OpenSystemAsync(cancellationToken);

        try
        {
            if (await TryLockAsync(connection, lockKey, cancellationToken))
            {
                return (true, new SessionLockHandle(connection, lockKey));
            }

            await connection.DisposeAsync();
            return (false, null);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public Task<IAsyncDisposable> AcquireSessionLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => AcquireAsync(resourceIdentifier, timeout, cancellationToken);

    private async Task<IAsyncDisposable> AcquireAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var lockKey = ComputeLockKey(resourceIdentifier);
        var connection = await _connections.OpenSystemAsync(cancellationToken);

        try
        {
            var acquired = await TryLockAsync(connection, lockKey, cancellationToken);
            var elapsed = TimeSpan.Zero;

            while (!acquired && elapsed < timeout)
            {
                await Task.Delay(_options.RetryInterval, cancellationToken);
                elapsed += _options.RetryInterval;
                acquired = await TryLockAsync(connection, lockKey, cancellationToken);
            }

            if (!acquired)
            {
                await connection.DisposeAsync();
                throw new LockAcquisitionException(resourceIdentifier, timeout);
            }

            return new SessionLockHandle(connection, lockKey);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, long lockKey, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
        cmd.Parameters.AddWithValue("key", lockKey);
        return await cmd.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static long ComputeLockKey(string resourceIdentifier)
    {
        var bytes = Encoding.UTF8.GetBytes(resourceIdentifier);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt64(hash, 0);
    }

    private sealed class SessionLockHandle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _lockKey;

        public SessionLockHandle(NpgsqlConnection connection, long lockKey)
        {
            _connection = connection;
            _lockKey = lockKey;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                cmd.Parameters.AddWithValue("key", _lockKey);
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
