using MorphDB.Npgsql.Services;
using Npgsql;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// A change log that holds its first entry until the test lets it go, and says when it got there. The
/// entry is the last write of a schema change, so the change is open -- its transaction uncommitted -- for
/// as long as it is held: a second request made meanwhile meets it in the middle.
/// </summary>
internal sealed class GatedChangeLogger(IChangeLogger inner, TaskCompletionSource reached, Task gate) : IChangeLogger
{
    private int _held;

    public async Task LogChangeAsync(SchemaChangeEntry entry, NpgsqlTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _held, 1) == 0)
        {
            reached.TrySetResult();
            await gate.WaitAsync(cancellationToken);
        }

        await inner.LogChangeAsync(entry, transaction, cancellationToken);
    }

    public Task<IReadOnlyList<SchemaChangeEntry>> GetHistoryAsync(Guid tableId, int limit = 100, CancellationToken cancellationToken = default) =>
        inner.GetHistoryAsync(tableId, limit, cancellationToken);

    public Task<IReadOnlyList<SchemaChangeEntry>> GetChangelogAsync(Guid projectId, int limit = 100, int offset = 0, CancellationToken cancellationToken = default) =>
        inner.GetChangelogAsync(projectId, limit, offset, cancellationToken);
}
