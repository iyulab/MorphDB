namespace MorphDB.Core.Abstractions;

/// <summary>
/// One database transaction that every read and write of the current asynchronous flow joins until the
/// unit ends: a row written inside it is seen by a read inside it, and nothing of it is kept unless it
/// commits. Ending it without <see cref="CommitAsync"/> rolls it back.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Keeps everything done in the unit. A unit begun inside another one joins it, so its commit keeps
    /// nothing on its own — the outermost unit decides.
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Begins units of work for a caller's rows.</summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// Begins a unit for the calling flow and what it awaits from here on, or joins the unit the flow is
    /// already in. Synchronous on purpose: the unit is ambient to the caller's flow, and a value set inside
    /// an awaited method does not flow back out of it. The connection is opened when the unit is first used.
    /// </summary>
    IUnitOfWork Begin();
}
