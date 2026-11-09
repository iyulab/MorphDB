using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace MorphDB.Service.OData;

/// <summary>
/// The system query options a request may carry, held to what the service serves. An option that is not served is
/// refused rather than answered without: the answer to a narrowing option left out is every row, with a
/// <c>200</c>. OData 4.01 Part 2 §5.1: the names of system options are optionally prefixed with <c>$</c> and a 4.01
/// service takes them with or without it, in any case — so <c>top=0</c> is <c>$top=0</c>, and <c>search=x</c> is
/// the unserved <c>$search</c>, not a custom option that happens to share its name. A name that is no system option
/// (<c>debug=1</c>) and a parameter alias (<c>@name</c>) belong to the request and are not this check's business.
/// OData 4.0 Part 1 §6.1: a service fails a request carrying an unsupported option the specification defines, and
/// should fail one carrying an option it does not understand.
/// </summary>
internal static class ODataSystemOptions
{
    /// <summary>
    /// The names OData 4.01 Part 2 §5.1 (and the aggregation extension's <c>$apply</c>) defines for system query
    /// options, without the <c>$</c>. <c>$levels</c> is an option of <c>$expand</c>, not one of its own, so only its
    /// <c>$</c> spelling is a system option here.
    /// </summary>
    private static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "apply", "compute", "count", "deltatoken", "expand", "filter", "format", "id", "index",
        "orderby", "schemaversion", "search", "select", "skip", "skiptoken", "top",
    };

    /// <summary>
    /// The one spelling the rest of the service reads: <c>$name</c>, lower case, for any system option written with
    /// or without the <c>$</c> and in any case. Anything else — an option the specification does not define, a custom
    /// option, an alias — comes back as it was written.
    /// </summary>
    public static string Canonical(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var bare = name.StartsWith('$') ? name[1..] : name;
        return SystemNames.Contains(bare) ? "$" + bare.ToLowerInvariant() : name;
    }

    /// <summary>
    /// The query options by their canonical names. A system option written twice — <c>top=1&amp;$top=2</c> — keeps
    /// both values, which <see cref="Problem"/> then refuses; none is silently the winner.
    /// </summary>
    /// <param name="query">The request's query options, by name.</param>
    public static Dictionary<string, StringValues> Canonicalize(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in query)
        {
            var key = Canonical(name);
            result[key] = result.TryGetValue(key, out var existing) ? StringValues.Concat(existing, values) : values;
        }

        return result;
    }

    private static readonly string[] CollectionOptions = ["$filter", "$orderby", "$top", "$skip", "$select", "$count", "$format"];

    private static readonly string[] EntityOptions = ["$select", "$format"];

    private static readonly string Served = string.Join(", ", CollectionOptions);

    /// <summary>What the REST doors call the row version a write expects; an OData write sends it as <c>If-Match</c>.</summary>
    private const string RestPreconditionOption = "expectedVersion";

    /// <summary>
    /// What is wrong with a request's options, or null when nothing is: the first option the service does not serve
    /// (on one entity, one that only a collection takes), or a value that is not one the option takes.
    /// </summary>
    /// <param name="query">The request's query options, by name.</param>
    /// <param name="entity">Whether the request reads one entity (its key in the path) rather than a collection.</param>
    public static string? Problem(IEnumerable<KeyValuePair<string, StringValues>> query, bool entity)
    {
        var served = entity ? EntityOptions : CollectionOptions;
        foreach (var (name, values) in Canonicalize(query))
        {
            if (!name.StartsWith('$'))
            {
                continue;
            }

            if (values.Count > 1)
            {
                return $"The query option '{name}' is given more than once.";
            }

            if (!served.Contains(name, StringComparer.Ordinal))
            {
                return CollectionOptions.Contains(name, StringComparer.Ordinal)
                    ? $"The query option '{name}' reads a collection of entities; one entity takes {string.Join(", ", EntityOptions)}."
                    : $"The query option '{name}' is not served. This service answers {Served}.";
            }

            var value = values.ToString();
            var problem = name switch
            {
                "$top" or "$skip" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                    ? null
                    : $"'{name}' is a non-negative whole number; '{value}' is not.",
                "$count" => value is "true" or "false"
                    ? null
                    : $"'$count' is true or false; '{value}' is neither.",
                "$format" => IsJson(value) ? null : $"'$format' is json — this service writes no other format; '{value}' is not.",
                _ => null,
            };
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// What is wrong with the query options of a request that changes an entity, or null when nothing is: a write
    /// takes no system option (<c>$format</c> aside). A <c>$filter</c> on a DELETE would otherwise read as «delete
    /// what matches» and be answered by deleting the entity the key names, whatever the filter says.
    /// </summary>
    /// <param name="query">The request's query options, by name.</param>
    public static string? WriteProblem(IEnumerable<KeyValuePair<string, StringValues>> query)
    {
        foreach (var (name, values) in Canonicalize(query))
        {
            // The REST doors' own name for a precondition. Here it is no option, so it would read as a custom one and
            // be left alone: the write goes through unconditionally, which is the lost update it was sent to prevent.
            if (name.Equals(RestPreconditionOption, StringComparison.OrdinalIgnoreCase))
            {
                return $"'{RestPreconditionOption}' is a parameter of the REST doors, not an OData option: an OData write names the row version it expects in the 'If-Match' header (the row's entity tag).";
            }

            if (!name.StartsWith('$'))
            {
                continue;
            }

            if (name != "$format")
            {
                return $"The query option '{name}' is not served on a write: a write takes no system query option.";
            }

            if (!IsJson(values.ToString()))
            {
                return $"'$format' is json — this service writes no other format; '{values}' is not.";
            }
        }

        return null;
    }

    private static bool IsJson(string format)
    {
        var media = format.Split(';', 2)[0].Trim();
        return media.Equals("json", StringComparison.OrdinalIgnoreCase) || media.Equals("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
