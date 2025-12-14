using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents metadata for a dynamic table.
/// </summary>
public sealed class TableMetadata
{
    public Guid TableId { get; init; }
    public Guid TenantId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public int SchemaVersion { get; init; }
    public JsonDocument? Descriptor { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsActive { get; init; } = true;

    public IReadOnlyList<ColumnMetadata> Columns { get; init; } = [];
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];
    public IReadOnlyList<IndexMetadata> Indexes { get; init; } = [];
}
