using System.Globalization;
using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Models.Api;

/// <summary>
/// The <c>column:operator:value</c> filter language of the REST endpoints, read the same way wherever
/// it is accepted.
/// <para>
/// One expression is one condition, and a request states several by repeating the parameter
/// (<c>?filter=a:gte:2&amp;filter=a:lt:4</c>) — the form the .NET client has always sent. Each condition
/// applies. Only the first two colons separate, so a value may contain colons, commas or anything else;
/// a list packed into one expression is not read, because a comma inside a value could not be told
/// apart from one between conditions.
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

    public static (string Column, FilterOperator Operator, object Value) Parse(string expression)
    {
        var parts = expression.Split(':', 3);
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new ArgumentException(
                $"Invalid filter format: '{expression}'. Expected 'column:operator:value' — one condition " +
                "per filter parameter; repeat the parameter for more. For OData syntax, use the /odata " +
                "endpoint with $filter instead.");
        }

        return (parts[0].Trim(), ApiModelExtensions.ParseFilterOperator(parts[1].Trim()), ParseValue(parts[2].Trim()));
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
