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

    // Virtual Constraint Properties

    /// <summary>
    /// Virtual NOT NULL - enforced at application layer, not in database.
    /// When true, the write pipeline will reject null values for this column.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Specifies how the default value should be applied.
    /// </summary>
    public DefaultValueType DefaultType { get; init; } = DefaultValueType.None;

    /// <summary>
    /// When true, unique constraint is enforced at application layer.
    /// Physical index may still exist for performance.
    /// </summary>
    public bool EnforceUniqueOnWrite { get; init; } = true;

    /// <summary>
    /// Condition for unique check (e.g., exclude soft-deleted rows).
    /// Example: "_deleted_at IS NULL"
    /// </summary>
    public string? UniqueCondition { get; init; }
}

/// <summary>
/// Specifies how a default value is applied.
/// </summary>
public enum DefaultValueType
{
    /// <summary>No default value.</summary>
    None,

    /// <summary>Static value stored in DefaultValue property.</summary>
    Static,

    /// <summary>Database function (e.g., gen_random_uuid(), CURRENT_TIMESTAMP).</summary>
    DbFunction,

    /// <summary>Computed from other fields in the same row.</summary>
    Computed,

    /// <summary>Context-based value (e.g., current user ID, tenant ID).</summary>
    ContextBased,

    /// <summary>Auto-incrementing sequence.</summary>
    AutoIncrement
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
