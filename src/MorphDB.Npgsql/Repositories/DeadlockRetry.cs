using MorphDB.Npgsql.Infrastructure;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Runs one autocommit statement again when PostgreSQL picked it as the victim of a deadlock. A
/// deadlock victim's statement is cancelled whole -- it changed nothing -- so sending it again is exactly
/// what PostgreSQL documents applications should do, and the caller never learns it was a victim. Only
/// the deadlock (<c>40P01</c>) and a serialization failure (<c>40001</c>) are retried, a few times with a
/// short jittered wait; any other failure, and the last attempt's, is thrown as it was.
/// </summary>
internal static class DeadlockRetry
{
    private const int Attempts = 4;

    /// <summary>
    /// Runs a metadata read, sent again when it is chosen as a deadlock victim -- a read changes nothing, so
    /// sending it again is always safe. Inside a unit of work the victim is the whole transaction, not the
    /// statement (PostgreSQL has abandoned it), so there the read runs once and the failure is thrown.
    /// </summary>
    public static Task<T> RunReadAsync<T>(Func<Task<T>> read)
        => ConnectionScope.HasScope ? read() : RunAsync(read);

    public static async Task<T> RunAsync<T>(Func<Task<T>> statement)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await statement().ConfigureAwait(false);
            }
            catch (PostgresException pg) when (attempt < Attempts
                && pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure)
            {
                await Task.Delay(Random.Shared.Next(10, 40) * attempt).ConfigureAwait(false);
            }
        }
    }
}
