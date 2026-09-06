using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace MorphDB.Service.Realtime;

/// <summary>
/// Evaluates a webhook's <c>filter</c> against a change event's row data.
/// </summary>
/// <remarks>
/// The whole grammar is flat, AND-combined scalar-literal equality — <c>{"col": "val", ...}</c>.
/// There is deliberately no operator syntax and no nesting; a filter whose value is an object or
/// array is rejected at registration by <see cref="IsSupported"/> rather than silently failing to
/// match at delivery time.
/// </remarks>
internal static class WebhookFilterMatcher
{
    /// <summary>
    /// No filter admits every row. A filter on a row with no data — a DELETE, which carries no
    /// pre-image — never matches: there is nothing to compare the filter against, and treating an
    /// unmatchable comparison as a pass would re-open the "filter fires for rows it should not"
    /// defect this evaluator exists to close.
    /// </summary>
    public static bool Matches(JsonDocument? filter, IDictionary<string, object?>? data)
    {
        if (IsAbsent(filter))
        {
            return true;
        }

        if (data is null)
        {
            return false;
        }

        foreach (var property in filter.RootElement.EnumerateObject())
        {
            if (!data.TryGetValue(property.Name, out var value) || !ValueEquals(property.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if every top-level filter value is a scalar literal (string/number/bool/null).</summary>
    public static bool IsSupported(JsonDocument? filter)
    {
        if (IsAbsent(filter))
        {
            return true;
        }

        if (filter.RootElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in filter.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when there is effectively no filter. ASP.NET Core model binding turns a request body's
    /// <c>"filter": null</c> into a non-null <see cref="JsonDocument"/> whose root is
    /// <see cref="JsonValueKind.Null"/> — not a C# <see langword="null"/> — because
    /// <c>JsonDocument.Parse("null")</c> is itself valid. Both shapes mean the same thing here.
    /// </summary>
    private static bool IsAbsent([NotNullWhen(false)] JsonDocument? filter) =>
        filter is null || filter.RootElement.ValueKind == JsonValueKind.Null;

    private static bool ValueEquals(JsonElement filterValue, object? dataValue)
    {
        // A JSON null inside a Dictionary<string, object?> value deserializes as a C# null, not a
        // JsonElement wrapping ValueKind.Null (unlike a JsonDocument? property — see IsAbsent).
        if (dataValue is null)
        {
            return filterValue.ValueKind == JsonValueKind.Null;
        }

        if (dataValue is not JsonElement dataElement)
        {
            return false;
        }

        if (filterValue.ValueKind != dataElement.ValueKind)
        {
            return false;
        }

        return filterValue.ValueKind switch
        {
            JsonValueKind.String => filterValue.GetString() == dataElement.GetString(),
            JsonValueKind.Number => filterValue.GetDouble() == dataElement.GetDouble(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => false,
        };
    }
}
