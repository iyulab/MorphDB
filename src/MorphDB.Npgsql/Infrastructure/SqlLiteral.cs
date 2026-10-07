using System.Collections;
using System.Globalization;
using System.Text.Json;
using MorphDB.Core.Exceptions;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Renders a value a caller declared — a rollup's filter value — as a PostgreSQL literal that can
/// be stored in a derived column's SQL. Only values of a known shape are rendered: a string is
/// quoted with its quotes doubled, a number is written in invariant form, and anything else
/// (an object, a value whose text would be pasted unquoted) is refused. A declared value never
/// reaches SQL as raw text.
/// </summary>
internal static class SqlLiteral
{
    /// <summary>The literal for one scalar value.</summary>
    public static string Render(object? value) => value switch
    {
        null => "NULL",
        JsonElement element => Render(element),
        string s => Quote(s),
        bool b => b ? "TRUE" : "FALSE",
        sbyte or byte or short or ushort or int or uint or long or ulong =>
            Convert.ToString(value, CultureInfo.InvariantCulture)!,
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        double d when double.IsFinite(d) => d.ToString("R", CultureInfo.InvariantCulture),
        float f when float.IsFinite(f) => f.ToString("R", CultureInfo.InvariantCulture),
        Guid g => $"'{g}'::uuid",
        DateTimeOffset dto => $"'{dto.ToString("O", CultureInfo.InvariantCulture)}'::timestamptz",
        DateTime dt => $"'{dt.ToString("O", CultureInfo.InvariantCulture)}'::timestamptz",
        _ => throw Refused(value),
    };

    /// <summary>
    /// The literals of a list value (an <c>in</c> / <c>notIn</c> operand), each rendered as by
    /// <see cref="Render(object?)"/>.
    /// </summary>
    public static IReadOnlyList<string> RenderList(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } array => array.EnumerateArray().Select(e => Render(e)).ToList(),
        string or null => throw new ValidationException("value", "this operator takes a list of values."),
        IEnumerable items => items.Cast<object?>().Select(Render).ToList(),
        _ => throw new ValidationException("value", "this operator takes a list of values."),
    };

    private static string Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => "NULL",
        JsonValueKind.String => Quote(element.GetString()!),
        JsonValueKind.True => "TRUE",
        JsonValueKind.False => "FALSE",
        JsonValueKind.Number => element.TryGetDecimal(out var m)
            ? m.ToString(CultureInfo.InvariantCulture)
            : element.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        _ => throw Refused(element.ValueKind),
    };

    // standard_conforming_strings is on (the PostgreSQL default since 9.1): a backslash is an
    // ordinary character in a quoted literal, and a doubled quote is the only escape.
    private static string Quote(string s) => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'";

    private static ValidationException Refused(object? shape) =>
        new("value", $"a value of this shape ({shape}) cannot be declared; use a string, number, boolean or null.");
}
