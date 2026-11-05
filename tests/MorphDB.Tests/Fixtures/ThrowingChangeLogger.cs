using MorphDB.Npgsql.Services;
using Npgsql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// A change log that refuses every entry. It is the last write of every schema change, so it stands for
/// any failure that arrives after the physical DDL has run. Given an operation, it refuses only that
/// one, so a change made of several can be made to fail at its last step.
/// </summary>
internal sealed class ThrowingChangeLogger(SchemaOperation? only = null) : IChangeLogger
{
    public Task LogChangeAsync(SchemaChangeEntry entry, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default) =>
        only is null || entry.Operation == only
            ? throw new InvalidOperationException("change log is down")
            : Task.CompletedTask;

    public Task<IReadOnlyList<SchemaChangeEntry>> GetHistoryAsync(Guid tableId, int limit = 100, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SchemaChangeEntry>>([]);

    public Task<IReadOnlyList<SchemaChangeEntry>> GetChangelogAsync(Guid projectId, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SchemaChangeEntry>>([]);
}
