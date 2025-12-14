using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Maps MorphDB logical types to PostgreSQL native types.
/// </summary>
public static class TypeMapper
{
    /// <summary>
    /// Gets the PostgreSQL native type for a MorphDB data type.
    /// </summary>
    public static string ToNativeType(MorphDataType dataType) => dataType switch
    {
        MorphDataType.Text => "text",
        MorphDataType.LongText => "text",
        MorphDataType.Integer => "integer",
        MorphDataType.BigInteger => "bigint",
        MorphDataType.Decimal => "numeric",
        MorphDataType.Boolean => "boolean",
        MorphDataType.Date => "date",
        MorphDataType.DateTime => "timestamptz",
        MorphDataType.Time => "time",
        MorphDataType.Uuid => "uuid",
        MorphDataType.Json => "jsonb",
        MorphDataType.Array => "jsonb",
        MorphDataType.Email => "text",
        MorphDataType.Url => "text",
        MorphDataType.Phone => "text",
        MorphDataType.SingleSelect => "text",
        MorphDataType.MultiSelect => "jsonb",
        MorphDataType.Relation => "uuid",
        MorphDataType.Rollup => "jsonb",
        MorphDataType.Formula => "text", // Stored as generated column
        MorphDataType.Attachment => "jsonb",
        MorphDataType.CreatedTime => "timestamptz",
        MorphDataType.ModifiedTime => "timestamptz",
        MorphDataType.CreatedBy => "uuid",
        MorphDataType.ModifiedBy => "uuid",
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown data type")
    };

    /// <summary>
    /// Gets the default value expression for system columns.
    /// </summary>
    public static string? GetDefaultExpression(MorphDataType dataType) => dataType switch
    {
        MorphDataType.CreatedTime => "now()",
        MorphDataType.ModifiedTime => "now()",
        _ => null
    };

    /// <summary>
    /// Checks if the type requires special index handling (e.g., GIN for JSONB).
    /// </summary>
    public static IndexType GetRecommendedIndexType(MorphDataType dataType) => dataType switch
    {
        MorphDataType.Json => IndexType.GIN,
        MorphDataType.Array => IndexType.GIN,
        MorphDataType.MultiSelect => IndexType.GIN,
        MorphDataType.Attachment => IndexType.GIN,
        _ => IndexType.BTree
    };

    /// <summary>
    /// Converts a .NET value to a PostgreSQL-compatible value.
    /// </summary>
    /// <remarks>
    /// Returns null as-is rather than DBNull.Value because Dapper handles null correctly
    /// when using ExpandoObject parameters.
    /// </remarks>
    public static object? ToDbValue(object? value, MorphDataType dataType)
    {
        if (value is null) return null;

        return dataType switch
        {
            MorphDataType.Json or MorphDataType.Array or MorphDataType.MultiSelect or MorphDataType.Attachment
                => System.Text.Json.JsonSerializer.Serialize(value),
            _ => value
        };
    }

    /// <summary>
    /// Converts a database value to a .NET value.
    /// </summary>
    public static object? FromDbValue(object? value, MorphDataType dataType)
    {
        if (value is null or DBNull) return null;

        return dataType switch
        {
            MorphDataType.Json or MorphDataType.Array or MorphDataType.MultiSelect or MorphDataType.Attachment
                when value is string json => System.Text.Json.JsonSerializer.Deserialize<object>(json),
            _ => value
        };
    }
}
