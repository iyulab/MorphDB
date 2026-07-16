using System.Text.Json;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Converts a System.Text.Json <see cref="JsonElement"/> — produced by REST model binding and by
/// JSONB deserialization — into a comparable/renderable CLR value. Non-JsonElement values pass
/// through unchanged.
/// </summary>
/// <remarks>
/// Values that cross the API boundary as <c>object?</c> arrive as <see cref="JsonElement"/>. Any
/// component that then inspects them with CLR type checks (<c>is string</c>, <c>is bool</c>) or
/// feeds them to Dapper/SQL must unwrap first, otherwise the JsonElement silently fails the check
/// (bypassed validation, unquoted SQL) or is rejected outright. Centralizing the unwrap here keeps
/// the write-pipeline validators and the view SQL builders consistent instead of each re-deriving
/// (and occasionally forgetting) it. For DB parameter binding that needs the column's target type,
/// use <see cref="TypeMapper.ToDbValue"/> instead — this converter is type-agnostic.
/// </remarks>
public static class JsonValueConverter
{
    /// <summary>
    /// Unwraps a <see cref="JsonElement"/> into its CLR equivalent; returns other values as-is.
    /// </summary>
    public static object? ToClrValue(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }
}
