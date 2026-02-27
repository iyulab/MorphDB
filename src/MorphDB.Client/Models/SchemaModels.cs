namespace MorphDB.Client.Models;

/// <summary>
/// Request to create a new table.
/// </summary>
public sealed class CreateTableRequest
{
    /// <summary>
    /// Table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column definitions.
    /// </summary>
    public IReadOnlyList<CreateColumnRequest> Columns { get; init; } = [];

    /// <summary>
    /// Optional table description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Request to create a column.
/// </summary>
public sealed class CreateColumnRequest
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column type (text, integer, boolean, timestamp, uuid, jsonb, etc.).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>
    /// Whether the column has a unique constraint.
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// Whether to create an index on this column.
    /// </summary>
    public bool IsIndexed { get; init; }

    /// <summary>
    /// Default value expression.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? CheckExpression { get; init; }

    /// <summary>
    /// Column description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Request to add a column to an existing table.
/// </summary>
public sealed class AddColumnRequest
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Column type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>
    /// Default value expression.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? CheckExpression { get; init; }
}

/// <summary>
/// Request to alter a column.
/// </summary>
public sealed class AlterColumnRequest
{
    /// <summary>
    /// New column name (for rename).
    /// </summary>
    public string? NewName { get; init; }

    /// <summary>
    /// New data type.
    /// </summary>
    public string? NewType { get; init; }

    /// <summary>
    /// New nullability setting.
    /// </summary>
    public bool? IsNullable { get; init; }

    /// <summary>
    /// New default value.
    /// </summary>
    public string? DefaultValue { get; init; }
}

/// <summary>
/// Table information response.
/// </summary>
public sealed class TableInfo
{
    /// <summary>
    /// Table ID.
    /// </summary>
    public Guid TableId { get; init; }

    /// <summary>
    /// Logical table name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Physical table name in database.
    /// </summary>
    public required string PhysicalName { get; init; }

    /// <summary>
    /// Current schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Column definitions.
    /// </summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Column information response.
/// </summary>
public sealed class ColumnInfo
{
    /// <summary>
    /// Column ID.
    /// </summary>
    public Guid ColumnId { get; init; }

    /// <summary>
    /// Logical column name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Physical column name in database.
    /// </summary>
    public required string PhysicalName { get; init; }

    /// <summary>
    /// Abstract data type.
    /// </summary>
    public required string DataType { get; init; }

    /// <summary>
    /// Native PostgreSQL type.
    /// </summary>
    public required string NativeType { get; init; }

    /// <summary>
    /// Whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Whether the column has a unique constraint.
    /// </summary>
    public bool IsUnique { get; init; }

    /// <summary>
    /// Whether the column is the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// Whether the column is indexed.
    /// </summary>
    public bool IsIndexed { get; init; }

    /// <summary>
    /// Check constraint expression.
    /// </summary>
    public string? CheckExpression { get; init; }

    /// <summary>
    /// Ordinal position in the table.
    /// </summary>
    public int OrdinalPosition { get; init; }
}
