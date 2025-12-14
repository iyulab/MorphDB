using System.Text.Json;

namespace MorphDB.Core.Models;

/// <summary>
/// Represents metadata for a column in a dynamic table.
/// </summary>
public sealed class ColumnMetadata
{
    public Guid ColumnId { get; init; }
    public Guid TableId { get; init; }
    public required string LogicalName { get; init; }
    public required string PhysicalName { get; init; }
    public required MorphDataType DataType { get; init; }
    public required string NativeType { get; init; }
    public bool IsNullable { get; init; } = true;
    public bool IsUnique { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsIndexed { get; init; }
    public bool IsEncrypted { get; init; }
    public string? DefaultValue { get; init; }
    public string? CheckExpression { get; init; }
    public int OrdinalPosition { get; init; }
    public JsonDocument? Descriptor { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// MorphDB logical data types.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Data type enum naturally uses type names")]
public enum MorphDataType
{
    Text,
    LongText,
    Integer,
    BigInteger,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Time,
    Uuid,
    Json,
    Array,
    Email,
    Url,
    Phone,
    SingleSelect,
    MultiSelect,
    Relation,
    Rollup,
    Formula,
    Attachment,
    CreatedTime,
    ModifiedTime,
    CreatedBy,
    ModifiedBy
}
