using System.Globalization;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Query;

/// <summary>
/// A filter's value read as the type of the column it compares with. A value that arrives as text — every value of
/// the REST filter language does — means what the column says it means: <c>12345</c> is a number against an integer
/// column and the text <c>12345</c> against a text one, and an id-shaped code stays a code in a text column. Reading
/// the type from the text alone compared a text column with a number or a uuid, and PostgreSQL refused the statement.
/// A typed value is passed as it is, except to a text column, which compares text. Text that does not read as the
/// column's type is the caller's mistake, refused with the column named.
/// </summary>
internal static class ColumnValues
{
    // System columns are created by the store, not declared, so they carry no metadata; these are the ones that hold text.
    private static readonly HashSet<string> TextSystemColumns = new(StringComparer.Ordinal) { SystemColumns.SourceId, SystemColumns.RowState };

    /// <summary>
    /// The value of a condition on <paramref name="logicalName"/> of <paramref name="table"/>: each value of a list, and
    /// the value of a comparison. A pattern (<c>like</c>, <c>contains</c>, …) is text whatever the column holds, and an
    /// exists keeps its related rows, whose conditions are read against their own table.
    /// </summary>
    public static object? OfCondition(TableMetadata table, string logicalName, FilterOperator op, object? value) => value switch
    {
        _ when op.TakesRelatedRows() || IsPattern(op) => value,
        object[] values => values.Select(v => Of(table, logicalName, v)!).ToArray(),
        _ => Of(table, logicalName, value),
    };

    private static bool IsPattern(FilterOperator op) =>
        op is FilterOperator.Like or FilterOperator.NotLike or FilterOperator.ILike or FilterOperator.Contains or FilterOperator.StartsWith or FilterOperator.EndsWith;

    /// <summary>The value <paramref name="value"/> means against <paramref name="logicalName"/> of <paramref name="table"/>.</summary>
    /// <exception cref="ValidationException">Text that does not read as the column's type.</exception>
    public static object? Of(TableMetadata table, string logicalName, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var column = table.Columns.FirstOrDefault(c => c.LogicalName == logicalName);
        var native = column is { IsDerived: false } ? column.NativeType : TextSystemColumns.Contains(logicalName) ? "text" : null;
        return native is null ? Shaped(value) : As(native, logicalName, value);
    }

    private static object? As(string native, string column, object value)
    {
        if (native is "text" || native.StartsWith("character", StringComparison.Ordinal) || native.StartsWith("varchar", StringComparison.Ordinal) || native == "citext")
        {
            return value switch
            {
                string written => written,
                // As the filter language and JSON spell them, so a typed value finds the text written the same way.
                bool flag => flag ? "true" : "false",
                DateTimeOffset at => at.ToString("O", CultureInfo.InvariantCulture),
                DateTime at => at.ToString("O", CultureInfo.InvariantCulture),
                Guid id => id.ToString("D"),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture),
            };
        }

        if (value is not string text)
        {
            return value;
        }

        object? read = native switch
        {
            "integer" or "int4" or "smallint" or "int2" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null,
            "bigint" or "int8" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null,
            "numeric" or "decimal" or "real" or "double precision" => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
            "boolean" or "bool" => bool.TryParse(text, out var b) ? b : null,
            "uuid" => Guid.TryParse(text, out var g) ? g : null,
            "date" => DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var day) ? day : null,
            "time" => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out var time) ? time : null,
            "timestamptz" or "timestamp" or "timestamp with time zone" =>
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToUniversalTime() : null,
            _ => Shaped(text),
        };
        return read ?? throw new ValidationException(column, $"'{text}' is not a value of the column's type ({native}).");
    }

    /// <summary>
    /// A value's type read from its text, for a column whose type the store does not record here (a system column, a
    /// derived one): numbers and instants in the invariant culture, an instant carried as UTC with its offset kept, and
    /// quotes taken off a quoted string.
    /// </summary>
    public static object? Shaped(object? value)
    {
        if (value is not string text)
        {
            return value;
        }

        if (bool.TryParse(text, out var boolValue))
            return boolValue;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue;

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return longValue;

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
            return decimalValue;

        if (Guid.TryParse(text, out var guidValue))
            return guidValue;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
            return instant.ToUniversalTime();

        return text;
    }
}
