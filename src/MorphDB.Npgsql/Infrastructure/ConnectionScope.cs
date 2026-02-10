using Npgsql;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Provides ambient connection/transaction scope for pipeline components
/// participating in a cross-entity transaction.
/// Uses AsyncLocal to propagate the scope through async call chains.
/// </summary>
internal static class ConnectionScope
{
    private static readonly AsyncLocal<ScopeData?> _current = new();

    public static NpgsqlConnection? CurrentConnection => _current.Value?.Connection;
    public static NpgsqlTransaction? CurrentTransaction => _current.Value?.Transaction;
    public static bool HasScope => _current.Value is not null;

    /// <summary>
    /// Begins a connection scope. All pipeline components will use
    /// the provided connection/transaction instead of opening new ones.
    /// </summary>
    public static IDisposable Begin(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var previous = _current.Value;
        _current.Value = new ScopeData(connection, transaction);
        return new ScopeDisposable(previous);
    }

    private sealed record ScopeData(NpgsqlConnection Connection, NpgsqlTransaction Transaction);

    private sealed class ScopeDisposable(ScopeData? previous) : IDisposable
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
