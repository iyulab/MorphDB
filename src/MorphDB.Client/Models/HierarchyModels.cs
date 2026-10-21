namespace MorphDB.Client.Models;

/// <summary>
/// The rows a hierarchy walk reached.
/// </summary>
public sealed class HierarchyResult
{
    public IReadOnlyList<HierarchyRecord> Records { get; init; } = [];
    public int TotalCount { get; init; }

    /// <summary>The deepest step the walk reached.</summary>
    public int MaxDepth { get; init; }

    /// <summary>The walk stopped at its <c>maxDepth</c> rather than at the tree's end.</summary>
    public bool ReachedMaxDepth { get; init; }
}

/// <summary>
/// One row a hierarchy walk reached.
/// </summary>
public sealed class HierarchyRecord
{
    /// <summary>The row in its declared names, as every other read returns it.</summary>
    public IDictionary<string, object?> Data { get; init; } = new Dictionary<string, object?>();

    /// <summary>Steps from the record the walk started at; 0 is that record.</summary>
    public int Depth { get; init; }

    /// <summary>Set only by a path walk: the ids from the starting record up to this row.</summary>
    public IReadOnlyList<Guid>? Path { get; init; }
}

/// <summary>
/// Request to ask whether a new parent would make a cycle (mirrors the server's <c>CycleCheckApiRequest</c>).
/// </summary>
public sealed class CycleCheckRequest
{
    public Guid RecordId { get; init; }
    public Guid NewParentId { get; init; }
    public string? ParentColumn { get; init; }
}

/// <summary>
/// The answer to a cycle check.
/// </summary>
public sealed class CycleCheckResult
{
    public bool WouldCreateCycle { get; init; }
}

/// <summary>
/// The records already in a cycle.
/// </summary>
public sealed class CycleDetectionResult
{
    public bool HasCycles { get; init; }
    public IReadOnlyList<Guid> CyclicRecordIds { get; init; } = [];
    public IReadOnlyList<string> CycleDescriptions { get; init; } = [];
}
