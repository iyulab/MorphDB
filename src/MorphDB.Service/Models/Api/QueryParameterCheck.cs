using System.Reflection;

namespace MorphDB.Service.Models.Api;

/// <summary>
/// The query parameters a read takes are the members of its parameter record, and a request that carries another is
/// refused naming it — the way a query body already refuses a member it does not know. A parameter spelled wrongly
/// (<c>fliter</c>, <c>filters</c>) narrows nothing, so the caller would be answered every row with a <c>200</c>.
/// </summary>
internal static class QueryParameterCheck
{
    /// <summary>The first parameter of <paramref name="query"/> that <typeparamref name="T"/> does not have, as a refusal; else null.</summary>
    public static string? Unknown<T>(IEnumerable<string> query)
    {
        var known = Names<T>.All;
        foreach (var name in query)
        {
            if (!known.Contains(name))
            {
                return $"Unknown query parameter '{name}'. Supported parameters: {string.Join(", ", Names<T>.Listed)}.";
            }
        }

        return null;
    }

    private static class Names<T>
    {
        public static readonly string[] Listed =
        [
            .. typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..]),
        ];

        public static readonly HashSet<string> All = new(Listed, StringComparer.OrdinalIgnoreCase);
    }
}
