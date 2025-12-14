using System.Security.Cryptography;
using System.Text;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// PostgreSQL advisory lock manager for DDL serialization.
/// </summary>
public sealed class PostgresAdvisoryLockManager : IAdvisoryLockManager
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly AdvisoryLockOptions _options;

    public PostgresAdvisoryLockManager(NpgsqlDataSource dataSource, AdvisoryLockOptions? options = null)
    {
        _dataSource = dataSource;
        _options = options ?? new AdvisoryLockOptions();
    }

    public async Task<IAsyncDisposable> AcquireDdlLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var lockKey = ComputeLockKey(resourceIdentifier);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var acquired = false;
            var elapsed = TimeSpan.Zero;

            while (!acquired && elapsed < timeout)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
                cmd.Parameters.AddWithValue("key", lockKey);

                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                acquired = result is true;

                if (!acquired)
                {
                    await Task.Delay(_options.RetryInterval, cancellationToken);
                    elapsed += _options.RetryInterval;
                }
            }

            if (!acquired)
            {
                await connection.DisposeAsync();
                throw new LockAcquisitionException(resourceIdentifier, timeout);
            }

            return new TransactionLockHandle(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<(bool Acquired, IAsyncDisposable? Lock)> TryAcquireDdlLockAsync(
        string resourceIdentifier,
        CancellationToken cancellationToken = default)
    {
        var lockKey = ComputeLockKey(resourceIdentifier);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
            cmd.Parameters.AddWithValue("key", lockKey);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            var acquired = result is true;

            if (acquired)
            {
                return (true, new TransactionLockHandle(connection));
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

    public async Task<IAsyncDisposable> AcquireSessionLockAsync(
        string resourceIdentifier,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var lockKey = ComputeLockKey(resourceIdentifier);
        var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var acquired = false;
            var elapsed = TimeSpan.Zero;

            while (!acquired && elapsed < timeout)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
                cmd.Parameters.AddWithValue("key", lockKey);

                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                acquired = result is true;

                if (!acquired)
                {
                    await Task.Delay(_options.RetryInterval, cancellationToken);
                    elapsed += _options.RetryInterval;
                }
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

    private static long ComputeLockKey(string resourceIdentifier)
    {
        var bytes = Encoding.UTF8.GetBytes(resourceIdentifier);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt64(hash, 0);
    }

    private sealed class TransactionLockHandle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;

        public TransactionLockHandle(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
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
