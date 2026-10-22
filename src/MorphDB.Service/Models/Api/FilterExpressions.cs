using System.Globalization;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Models.Api;

/// <summary>
/// The <c>column:operator:value</c> filter language of the REST endpoints, read the same way wherever
/// it is accepted.
/// <para>
/// One expression is one condition, and a request states several by repeating the parameter
/// (<c>?filter=a:gte:2&amp;filter=a:lt:4</c>) — the form the .NET client has always sent. Each condition
/// applies. Only the first two colons separate, so a value may contain colons, commas or anything else.
/// <c>in</c> and <c>notin</c> take their values as a JSON array (<c>status:in:["open","held"]</c>) — JSON
/// quoting is what lets a value hold a comma; a list written any other way is refused.
/// </para>
/// <para>
/// A malformed expression is refused rather than skipped: a condition the server drops makes the answer
/// wider than the question, and for a bulk update or delete that is more rows written than asked.
/// </para>
/// </summary>
internal static class FilterExpressions
{
    public static IMorphQuery Apply(IMorphQuery query, IEnumerable<string>? expressions)
    {
        var first = true;
        foreach (var expression in expressions ?? [])
        {
            var (column, op, value) = Parse(expression);
            query = first ? query.Where(column, op, value) : query.AndWhere(column, op, value);
            first = false;
        }

        return query;
    }

    /// <summary>Whether the request carries at least one condition.</summary>
    public static bool Any(IEnumerable<string>? expressions) =>
        expressions?.Any(e => !string.IsNullOrWhiteSpace(e)) == true;

    public static (string Column, FilterOperator Operator, object? Value) Parse(string expression)
    {
        var parts = expression.Split(':', 3);
        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
        {
            var op = ApiModelExtensions.ParseFilterOperator(parts[1].Trim());
            if (op is FilterOperator.IsNull or FilterOperator.IsNotNull)
            {
                // Takes no value: `column:isnull` or `column:isnull:`. A value has no reading here, and
                // ignoring it would answer a question the caller did not ask.
                if (parts.Length == 3 && parts[2].Trim().Length > 0)
                {
                    throw new ArgumentException(
                        $"Invalid filter format: '{expression}'. '{parts[1].Trim()}' takes no value — write '{parts[0].Trim()}:{parts[1].Trim()}'.");
                }

                return (parts[0].Trim(), op, null);
            }

            if (parts.Length == 3 && op == FilterOperator.Exists)
            {
                return (parts[0].Trim(), op, RelatedRowsJson.Parse(expression, parts[2].Trim()));
            }

            if (parts.Length == 3)
            {
                return op is FilterOperator.In or FilterOperator.NotIn
                    ? (parts[0].Trim(), op, ParseList(expression, parts[2].Trim()))
                    : (parts[0].Trim(), op, ParseValue(parts[2].Trim()));
            }
        }

        throw new ArgumentException(
            $"Invalid filter format: '{expression}'. Expected 'column:operator:value' ('column:isnull' and " +
            "'column:isnotnull' take no value) — one condition per filter parameter; repeat the parameter " +
            "for more. For OData syntax, use the /odata endpoint with $filter instead.");
    }

    private static object[] ParseList(string expression, string text)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return [.. document.RootElement.EnumerateArray().Select(ListElement)];
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        throw new ArgumentException(
            $"Invalid filter format: '{expression}'. The values of in and notin are a JSON array — " +
            "write 'status:in:[\"open\",\"held\"]' or 'id:in:[1,2]'.");
    }

    /// <summary>
    /// One value of an <c>in</c> list, as JSON writes it: a number is a number and a boolean a boolean; a
    /// string is read as a single value's text is (<see cref="ParseValue"/>) only where JSON has no form of
    /// its own — an instant or an id — and stays a string otherwise. <c>null</c> is refused: <c>in</c>
    /// never matches an empty column, and <c>isnull</c> is the condition that does.
    /// </summary>
    public static object ListElement(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var integral) ? integral : element.GetDecimal(),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        System.Text.Json.JsonValueKind.String => ListText(element.GetString()!),
        _ => throw new ArgumentException(
            $"A list of values holds numbers, strings and booleans, and this one holds a JSON {element.ValueKind.ToString().ToLowerInvariant()}; " +
            "an empty column is matched with isnull."),
    };

    private static object ListText(string text)
    {
        var typed = ParseValue(text);
        return typed is Guid or DateTimeOffset ? typed : text;
    }

    /// <summary>
    /// A value's type is read from its text, the same on every server: numbers and instants are parsed
    /// with the invariant culture, so <c>1.5</c> is a decimal wherever the service runs, and an instant
    /// is carried as UTC with its offset kept rather than reinterpreted in the server's time zone.
    /// </summary>
    public static object ParseValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return longValue;

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
            return decimalValue;

        if (Guid.TryParse(value, out var guidValue))
            return guidValue;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
            return instant.ToUniversalTime();

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];

        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
            return value[1..^1];

        return value;
    }
}
