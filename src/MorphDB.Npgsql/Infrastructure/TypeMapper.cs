using System.Text.Json;
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
    /// Checks if a type change from one MorphDataType to another can be safely cast in PostgreSQL.
    /// Returns true if the conversion is safe (no data loss expected).
    /// </summary>
    public static bool IsTypeCastSafe(MorphDataType fromType, MorphDataType toType)
    {
        if (fromType == toType)
            return true;

        var from = ToNativeType(fromType);
        var to = ToNativeType(toType);

        if (from == to)
            return true;

        // Safe widening conversions
        return (from, to) switch
        {
            // Integer widening
            ("integer", "bigint") => true,
            ("integer", "numeric") => true,
            ("bigint", "numeric") => true,

            // Any scalar to text is safe
            (_, "text") => true,

            // Date/time conversions
            ("date", "timestamptz") => true,

            // Everything else is potentially unsafe
            _ => false
        };
    }

    /// <summary>
    /// Converts a .NET value to a PostgreSQL-compatible value.
    /// </summary>
    /// <remarks>
    /// Returns null as-is rather than DBNull.Value because Dapper handles null correctly
    /// when using ExpandoObject parameters.
    /// </remarks>
    public static object? ToDbValue(object? value, MorphDataType dataType)
    {
        if (value is null)
            return null;

        // Handle JsonElement from System.Text.Json deserialization
        if (value is JsonElement jsonElement)
        {
            value = ConvertJsonElement(jsonElement, dataType);
            if (value is null)
                return null;
        }

        return dataType switch
        {
            MorphDataType.Json or MorphDataType.Array or MorphDataType.MultiSelect or MorphDataType.Attachment
                => JsonSerializer.Serialize(value),
            _ => value
        };
    }

    /// <summary>
    /// Converts a JsonElement to the appropriate .NET type based on the MorphDataType.
    /// </summary>
    private static object? ConvertJsonElement(JsonElement element, MorphDataType dataType)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        return dataType switch
        {
            MorphDataType.Text or MorphDataType.LongText or MorphDataType.Email or
            MorphDataType.Url or MorphDataType.Phone or MorphDataType.SingleSelect or MorphDataType.Formula
                => element.GetString(),

            MorphDataType.Integer => element.TryGetInt32(out var intVal) ? intVal : element.GetInt64(),

            MorphDataType.BigInteger => element.GetInt64(),

            MorphDataType.Decimal => element.GetDecimal(),

            MorphDataType.Boolean => element.GetBoolean(),

            MorphDataType.Date or MorphDataType.DateTime or MorphDataType.Time or
            MorphDataType.CreatedTime or MorphDataType.ModifiedTime
                => element.ValueKind == JsonValueKind.String
                    ? DateTime.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : element.GetDateTime(),

            MorphDataType.Uuid or MorphDataType.Relation or MorphDataType.CreatedBy or MorphDataType.ModifiedBy
                => element.ValueKind == JsonValueKind.String ? Guid.Parse(element.GetString()!) : element.GetGuid(),

            MorphDataType.Json or MorphDataType.Array or MorphDataType.MultiSelect or
            MorphDataType.Attachment or MorphDataType.Rollup
                => element, // Return JsonElement as-is; will be serialized in the switch above

            _ => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.GetRawText()
            }
        };
    }

    /// <summary>
    /// Converts a database value to a .NET value.
    /// </summary>
    public static object? FromDbValue(object? value, MorphDataType dataType)
    {
        if (value is null or DBNull)
            return null;

        return dataType switch
        {
            MorphDataType.Json or MorphDataType.Array or MorphDataType.MultiSelect or MorphDataType.Attachment
                when value is string json => System.Text.Json.JsonSerializer.Deserialize<object>(json),
            _ => value
        };
    }
}
