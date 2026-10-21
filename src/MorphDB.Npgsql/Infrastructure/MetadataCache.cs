using System.Collections.Concurrent;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Table definitions kept in memory, so a request that reads a table does not read its definition from
/// the database first. A definition is read on every request that touches a table, and reading it was
/// most of what such a request cost.
/// </summary>
/// <remarks>
/// <para>The cache serves only while something keeps it current: <see cref="Start"/> is called by whatever
/// is told of every change to a definition — the service's metadata listener, which hears a notification
/// the database sends for every write to the definition tables, whoever made it. Until then, and again
/// from <see cref="Stop"/> (the listener lost its connection and may have missed a change), nothing is
/// served from it and every read goes to the database, as it did before there was a cache. A host that
/// runs no listener never starts it.</para>
/// <para>A write made through this process empties the cache as soon as it is committed
/// (<see cref="Invalidate"/>), so the request after it reads what it wrote; a write made elsewhere — another
/// instance of the service — is seen once its notification arrives.</para>
/// <para>A definition read from the database is kept only if nothing was invalidated while it was being
/// read (<see cref="Generation"/>): a read that began before a change and finished after it holds the old
/// definition, and keeping it would serve it until the next change.</para>
/// </remarks>
public sealed class MetadataCache
{
    private readonly ConcurrentDictionary<(Guid ProjectId, string LogicalName), TableMetadata> _byName = new();
    private readonly ConcurrentDictionary<Guid, TableMetadata> _byId = new();
    private readonly Lock _gate = new();
    private long _generation;
    private volatile bool _live;

    /// <summary>Whether definitions are served from memory — between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsLive => _live;

    /// <summary>Changes on every invalidation; a read records it before going to the database.</summary>
    public long Generation => Interlocked.Read(ref _generation);

    /// <summary>Begins serving, from empty: whatever was kept before may have missed a change.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ClearLocked();
            _live = true;
        }
    }

    /// <summary>Stops serving and forgets everything — nothing now says when a definition changes.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _live = false;
            ClearLocked();
        }
    }

    /// <summary>
    /// Forgets the definitions of <paramref name="projectId"/>'s tables, or of every table when it is null —
    /// a change whose project is not known, or one that may reach across projects.
    /// </summary>
    public void Invalidate(Guid? projectId = null)
    {
        lock (_gate)
        {
            if (projectId is not { } project)
            {
                ClearLocked();
                return;
            }

            _generation++;
            foreach (var key in _byName.Keys.Where(k => k.ProjectId == project))
            {
                _byName.TryRemove(key, out _);
            }

            foreach (var (id, table) in _byId.Where(e => e.Value.ProjectId == project))
            {
                _byId.TryRemove(id, out _);
            }
        }
    }

    internal bool TryGet(Guid projectId, string logicalName, out TableMetadata table)
    {
        if (_live && _byName.TryGetValue((projectId, logicalName), out table!))
        {
            return true;
        }

        table = null!;
        return false;
    }

    internal bool TryGet(Guid tableId, out TableMetadata table)
    {
        if (_live && _byId.TryGetValue(tableId, out table!))
        {
            return true;
        }

        table = null!;
        return false;
    }

    /// <summary>Keeps <paramref name="table"/> unless the cache was invalidated since <paramref name="generationAtRead"/>.</summary>
    internal void Keep(TableMetadata table, long generationAtRead)
    {
        lock (_gate)
        {
            if (!_live || _generation != generationAtRead)
            {
                return;
            }

            _byName[(table.ProjectId, table.LogicalName)] = table;
            _byId[table.TableId] = table;
        }
    }

    private void ClearLocked()
    {
        _generation++;
        _byName.Clear();
        _byId.Clear();
    }
}
