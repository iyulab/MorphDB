using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace MorphDB.Service.OData;

/// <summary>
/// The resource a batch request's URL names: an entity set, optionally one of its entities by key, and
/// the query options. The URL is relative to the service root (<c>Customers(…)?$select=name</c>); one
/// that carries the root's path or a whole address is read relative to it too.
/// </summary>
internal sealed class ODataBatchUrl
{
    private readonly Dictionary<string, StringValues> _query;

    private ODataBatchUrl(string entitySet, Guid? key, Dictionary<string, StringValues> query)
    {
        EntitySet = entitySet;
        Key = key;
        _query = query;
    }

    public string EntitySet { get; }

    public Guid? Key { get; }

    /// <summary>Every query option the URL carries, by name.</summary>
    public IReadOnlyDictionary<string, StringValues> Options => _query;

    public string? Query(string option) => _query.TryGetValue(option, out var value) ? value.ToString() : null;

    public int? IntQuery(string option) => Query(option) is { } text && int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    public static bool TryParse(string? url, out ODataBatchUrl target, out string problem)
    {
        target = null!;
        if (string.IsNullOrWhiteSpace(url))
        {
            problem = "a request names its resource in 'url', relative to the service root (for example 'Customers').";
            return false;
        }

        var text = url.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            text = absolute.PathAndQuery;
        }

        var queryAt = text.IndexOf('?', StringComparison.Ordinal);
        var path = (queryAt < 0 ? text : text[..queryAt]).Trim('/');
        if (path.StartsWith("odata/", StringComparison.OrdinalIgnoreCase))
        {
            path = path["odata/".Length..];
        }

        var query = queryAt < 0 ? [] : QueryHelpers.ParseQuery(text[queryAt..]);

        var open = path.IndexOf('(', StringComparison.Ordinal);
        var entitySet = open < 0 ? path : path[..open];
        if (entitySet.Length == 0 || entitySet.Contains('/', StringComparison.Ordinal) || entitySet.StartsWith('$'))
        {
            problem = $"'{url}' does not name an entity set or one entity of it ('Customers', 'Customers(<id>)').";
            return false;
        }

        Guid? key = null;
        if (open >= 0)
        {
            if (!path.EndsWith(')') || !Guid.TryParse(path[(open + 1)..^1].Trim('\''), out var id))
            {
                problem = $"'{url}' names an entity by a key that is not an id ('{entitySet}(<id>)').";
                return false;
            }

            key = id;
        }

        target = new ODataBatchUrl(entitySet, key, query);
        problem = "";
        return true;
    }
}
