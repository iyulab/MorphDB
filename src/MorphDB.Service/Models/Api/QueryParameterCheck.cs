namespace MorphDB.Service.Models.Api;

/// <summary>
/// The query parameters a door takes are the ones it declares — the members of its parameter record, or its simple
/// parameters — and a request that carries another is refused naming it, the way a query body already refuses a
/// member it does not know. A parameter spelled wrongly (<c>fliter</c>, <c>filters</c>) narrows nothing, so the
/// caller would be answered every row with a <c>200</c>.
/// </summary>
internal static class QueryParameterCheck
{
    /// <summary>
    /// What to do instead of <c>?expectedVersion=</c>, the parameter this refusal exists for: a write states the row
    /// version it expects in <c>If-Match</c>, as the entity tag a read of the row returned.
    /// </summary>
    private const string ExpectedVersionHint =
        " To write only if the row is still at the version you read, send that row's entity tag (the ETag of a single-row read) in the 'If-Match' header.";

    /// <summary>The first parameter of <paramref name="query"/> that is not among <paramref name="known"/>, as a refusal; else null.</summary>
    public static string? Unknown(IEnumerable<string> query, IReadOnlyCollection<string> known) => Unknown(query, known, known);

    /// <summary>
    /// As above, where <paramref name="known"/> is every spelling that passes and <paramref name="listed"/> the names a
    /// refusal offers instead (a parameter may be read under more than one spelling).
    /// </summary>
    public static string? Unknown(IEnumerable<string> query, IReadOnlyCollection<string> known, IReadOnlyCollection<string> listed)
    {
        foreach (var name in query)
        {
            if (!known.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                var refusal = listed.Count == 0
                    ? $"Unknown query parameter '{name}'. This route takes no query parameters."
                    : $"Unknown query parameter '{name}'. Supported parameters: {string.Join(", ", listed)}.";
                return name.Equals("expectedVersion", StringComparison.OrdinalIgnoreCase) ? refusal + ExpectedVersionHint : refusal;
            }
        }

        return null;
    }
}
