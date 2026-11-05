using MorphDB.Core.Exceptions;
using Npgsql;

namespace MorphDB.Npgsql.Repositories;

/// <summary>
/// Turns a unique violation raised by a control-plane INSERT into the domain exception the API
/// already documents for it.
/// <para>
/// The schema manager checks for a duplicate name before it inserts, so reaching the constraint at
/// all means two callers raced for the same name. That is still the caller's answer to receive —
/// 409 with a code they can branch on — not a 500 with a driver message behind it. The global
/// handler has no case for PostgresException, so anything left untranslated here becomes an opaque
/// INTERNAL_ERROR and reads to the caller as our defect.
/// </para>
/// </summary>
internal static class MetadataConstraintTranslation
{
    /// <summary>
    /// Runs a control-plane INSERT, reporting a unique violation as a duplicate of
    /// <paramref name="entityType"/> named <paramref name="logicalName"/>.
    /// </summary>
    public static async Task<T> AsDuplicateNameAsync<T>(
        string entityType,
        string logicalName,
        Func<Task<T>> insert,
        bool retryDeadlocks = true)
    {
        try
        {
            // Inside a transaction a deadlock victim is the whole transaction, not this statement: sending the
            // statement again would run in a transaction PostgreSQL has already abandoned.
            return retryDeadlocks
                ? await DeadlockRetry.RunAsync(insert).ConfigureAwait(false)
                : await insert().ConfigureAwait(false);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateNameException(entityType, logicalName);
        }
    }

    /// <inheritdoc cref="AsDuplicateNameAsync{T}"/>
    public static async Task AsDuplicateNameAsync(
        string entityType,
        string logicalName,
        Func<Task> insert)
    {
        try
        {
            await DeadlockRetry.RunAsync(async () =>
            {
                await insert().ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateNameException(entityType, logicalName);
        }
    }
}
