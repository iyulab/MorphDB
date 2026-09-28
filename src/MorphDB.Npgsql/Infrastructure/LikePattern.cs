using System.Globalization;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The LIKE patterns behind the <c>contains</c>, <c>startswith</c> and <c>endswith</c> operators. The
/// caller's value is matched literally: <c>%</c>, <c>_</c> and the escape character itself are escaped
/// (PostgreSQL's default LIKE escape is the backslash), so a search for <c>50%</c> or <c>a_b</c> finds
/// that text rather than treating it as a wildcard. The <c>like</c>/<c>ilike</c> operators are the ones
/// that take a pattern, and they are left alone.
/// </summary>
internal static class LikePattern
{
    public static string Contains(object? value) => $"%{Escape(value)}%";

    public static string StartsWith(object? value) => $"{Escape(value)}%";

    public static string EndsWith(object? value) => $"%{Escape(value)}";

    public static string Escape(object? value)
    {
        var text = Convert.ToString(JsonValueConverter.ToClrValue(value), CultureInfo.InvariantCulture) ?? string.Empty;
        return text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }
}
