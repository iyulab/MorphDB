using MorphDB.Core.Abstractions;
using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// A unit of work over one PostgreSQL connection and transaction, made ambient with
/// <see cref="ConnectionScope"/> so every component that leases a connection
/// (<see cref="MorphConnectionFactory.LeaseAsync"/>) runs in it. The connection is opened for the caller
/// on first use, so row-level security sees the same caller inside the unit as outside it.
/// </summary>
public sealed class PostgresUnitOfWorkFactory(MorphConnectionFactory connections) : IUnitOfWorkFactory
{
    /// <inheritdoc />
    public IUnitOfWork Begin()
    {
        if (ConnectionScope.HasScope)
        {
            return JoinedUnit.Instance;
        }

        var transaction = new UnitTransaction(connections);
        return new OwnedUnit(transaction, ConnectionScope.Begin(transaction));
    }

    private sealed class OwnedUnit(UnitTransaction transaction, IDisposable scope) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);

        // Not async: the scope is reset in the caller's flow, where Begin set it — reset inside an
        // awaited method, it would stay set for the caller.
        public ValueTask DisposeAsync()
        {
            scope.Dispose();
            return transaction.DisposeAsync();
        }
    }

    /// <summary>A unit begun inside another: the outer one commits or rolls back.</summary>
    private sealed class JoinedUnit : IUnitOfWork
    {
        public static readonly JoinedUnit Instance = new();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// The connection and transaction of one unit of work, opened on the first lease. The unit's statements
/// run one after another, as an Npgsql connection requires.
/// </summary>
internal sealed class UnitTransaction(MorphConnectionFactory connections) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private bool _committed;

    public async ValueTask<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> OpenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_committed, this);
        if (_connection is null)
        {
            var connection = await connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _connection = connection;
        }

        return (_connection, _transaction!);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_transaction is not null && !_committed)
            {
                await _transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_transaction is not null)
            {
                await _transaction.DisposeAsync().ConfigureAwait(false);
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
