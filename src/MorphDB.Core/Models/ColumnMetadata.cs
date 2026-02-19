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

    /// <summary>
    /// When true, this is a system-managed column (prefixed with _).
    /// System columns are not hashed and have physical name = logical name.
    /// </summary>
    public bool IsSystemColumn { get; init; }

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

    // Computed/Derived Column Properties

    /// <summary>
    /// Configuration for computed columns (PostgreSQL GENERATED columns).
    /// Only set when DataType implies a computed column.
    /// </summary>
    public ComputedColumnConfig? ComputedConfig { get; init; }

    /// <summary>
    /// Configuration for lookup fields that reference related table data.
    /// Only set when DataType is Relation with lookup enabled.
    /// </summary>
    public LookupColumnConfig? LookupConfig { get; init; }

    /// <summary>
    /// Configuration for rollup fields that aggregate related records.
    /// Only set when DataType is Rollup.
    /// </summary>
    public RollupColumnConfig? RollupConfig { get; init; }

    /// <summary>
    /// Configuration for formula fields with expression evaluation.
    /// Only set when DataType is Formula.
    /// </summary>
    public FormulaColumnConfig? FormulaConfig { get; init; }

    /// <summary>
    /// FK relationship metadata. Set when this column participates in a relation as a source (foreign key).
    /// Populated during schema queries when includeColumns is true.
    /// </summary>
    public ForeignKeyReference? ForeignKey { get; init; }

    /// <summary>
    /// Whether this column is a derived type (computed, lookup, rollup, or formula).
    /// Derived columns are read-only and computed from other data.
    /// </summary>
    public bool IsDerived =>
        ComputedConfig != null ||
        LookupConfig != null ||
        RollupConfig != null ||
        FormulaConfig != null;
}

/// <summary>
/// FK reference metadata embedded in a column definition.
/// Indicates that this column references another table's column.
/// </summary>
public sealed class ForeignKeyReference
{
    /// <summary>The relation ID from RelationMetadata.</summary>
    public Guid RelationId { get; init; }

    /// <summary>The logical name of the referenced table.</summary>
    public required string TargetTable { get; init; }

    /// <summary>The logical name of the referenced column.</summary>
    public required string TargetColumn { get; init; }

    /// <summary>The relation type (OneToOne, OneToMany, ManyToMany).</summary>
    public RelationType RelationType { get; init; }

    /// <summary>The delete action for this FK.</summary>
    public OnDeleteAction OnDelete { get; init; } = OnDeleteAction.NoAction;
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
    Lookup,
    Rollup,
    Formula,
    Computed,
    Attachment,
    CreatedTime,
    ModifiedTime,
    CreatedBy,
    ModifiedBy
}
