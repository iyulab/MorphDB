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
        Func<Task<T>> insert)
    {
        try
        {
            return await insert().ConfigureAwait(false);
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
            await insert().ConfigureAwait(false);
        }
        catch (PostgresException pg) when (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new DuplicateNameException(entityType, logicalName);
        }
    }
}
