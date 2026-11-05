using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Repositories;
using Npgsql;

namespace MorphDB.Npgsql.Services;

/// <summary>
/// A schema change -- the physical statements and the writes that describe them -- as one transaction. A
/// change that leaves one without the other is an object that nothing describes or a description of
/// something that is not there, and the same request could then never be made again. A deadlock victim is
/// the whole transaction, nothing of it survived, so the whole change is run again.
/// </summary>
internal static class SchemaUnit
{
    public static Task<T> RunAsync<T>(
        MorphConnectionFactory connections,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> change,
        CancellationToken cancellationToken) =>
        DeadlockRetry.RunAsync(async () =>
        {
            await using var connection = await connections.OpenSystemAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var value = await change(connection, transaction);
            await transaction.CommitAsync(cancellationToken);
            return value;
        });
}
