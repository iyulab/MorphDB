using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The unit of work (<see cref="PostgresUnitOfWorkFactory"/>) the current asynchronous flow is in, if any.
/// </summary>
internal static class ConnectionScope
{
    private static readonly AsyncLocal<UnitTransaction?> _current = new();

    public static UnitTransaction? Current => _current.Value;

    public static bool HasScope => _current.Value is not null;

    /// <summary>
    /// Makes <paramref name="transaction"/> ambient to the calling flow until the returned handle is
    /// disposed. Call it from the flow that will run the statements — set inside an awaited method, an
    /// <see cref="AsyncLocal{T}"/> value does not flow back out to the caller.
    /// </summary>
    public static IDisposable Begin(UnitTransaction transaction)
    {
        var previous = _current.Value;
        _current.Value = transaction;
        return new ScopeDisposable(previous);
    }

    private sealed class ScopeDisposable(UnitTransaction? previous) : IDisposable
    {
        public void Dispose() => _current.Value = previous;
    }
}

/// <summary>
/// Wraps a database connection that may be owned (new) or shared (from ConnectionScope).
/// Only disposes the connection if it was newly opened (owned).
/// </summary>
internal readonly struct ScopedConnection(NpgsqlConnection connection, NpgsqlTransaction? transaction, bool ownsConnection) : IAsyncDisposable
{
    public NpgsqlConnection Connection => connection;
    public NpgsqlTransaction? Transaction => transaction;

    public ValueTask DisposeAsync()
    {
        if (ownsConnection)
            return connection.DisposeAsync();
        return ValueTask.CompletedTask;
    }
}
