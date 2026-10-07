using System.Text;
using System.Text.RegularExpressions;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// Rewrites the column references in a caller-authored SQL fragment — a view's join condition or
/// computed column, a row-level security predicate — from logical names to the physical names the
/// table actually has, leaving everything else (operators, literals, keywords, function names)
/// untouched.
/// <para>
/// One rule for every fragment that names columns: a policy and a view expression are written the
/// same way by the same caller, so they must be read the same way. A single-quoted string literal is
/// copied verbatim — <c>'status'</c> is a value, never a column. A double-quoted identifier is a
/// column reference whose text is the name, so <c>"status"</c> and <c>status</c> resolve alike. A
/// bare or dotted identifier (<c>status</c>, <c>orders.status</c>) is offered to the caller's map.
/// </para>
/// </summary>
internal static partial class SqlIdentifierRewriter
{
    /// <summary>
    /// Rewrites <paramref name="text"/>, replacing each identifier for which
    /// <paramref name="map"/> returns a value. An identifier the map returns <c>null</c> for is kept
    /// as written — it may be a keyword or a function name rather than a column.
    /// </summary>
    public static string Rewrite(string text, Func<string, string?> map)
    {
        var result = new StringBuilder(text.Length);
        var lastIndex = 0;

        foreach (Match match in Token().Matches(text))
        {
            result.Append(text, lastIndex, match.Index - lastIndex);
            result.Append(Translate(match.Value, map));
            lastIndex = match.Index + match.Length;
        }

        result.Append(text, lastIndex, text.Length - lastIndex);
        return result.ToString();
    }

    /// <summary>
    /// The identifiers <paramref name="text"/> names, in order — bare and dotted as written, a
    /// double-quoted one by its unquoted text. String literals are not identifiers.
    /// </summary>
    public static IEnumerable<string> Identifiers(string text)
    {
        foreach (Match match in Token().Matches(text))
        {
            if (match.Value.StartsWith('\''))
            {
                continue;
            }

            yield return match.Value.StartsWith('"') ? Unquote(match.Value) : match.Value;
        }
    }

    private static string Translate(string token, Func<string, string?> map)
    {
        if (token.StartsWith('\''))
        {
            return token;
        }

        var name = token.StartsWith('"') ? Unquote(token) : token;
        return map(name) ?? token;
    }

    private static string Unquote(string quoted) => quoted[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);

    // A single-quoted literal, a double-quoted identifier (both with doubled-quote escapes), or a
    // bare/dotted identifier.
    [GeneratedRegex(@"'(?:[^']|'')*'|""(?:[^""]|"""")*""|\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?\b")]
    private static partial Regex Token();
}
