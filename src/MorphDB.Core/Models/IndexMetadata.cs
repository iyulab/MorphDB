using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents an index on a table.
/// </summary>
public sealed class IndexMetadata
{
    public Guid IndexId { get; init; }
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required IReadOnlyList<IndexColumnInfo> Columns { get; init; }
    public IndexType IndexType { get; init; } = IndexType.BTree;
    public bool IsUnique { get; init; }
    public string? WhereClause { get; init; }
    public JsonDocument? Descriptor { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class IndexColumnInfo
{
    public Guid ColumnId { get; init; }
    public required string PhysicalName { get; init; }
    public SortDirection Direction { get; init; } = SortDirection.Ascending;
    public NullsPosition NullsPosition { get; init; } = NullsPosition.Last;
}

public enum IndexType
{
    BTree,
    Hash,
    GiST,
    GIN,
    BRIN
}

public enum SortDirection
{
    Ascending,
    Descending
}

public enum NullsPosition
{
    First,
    Last
}
