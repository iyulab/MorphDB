using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace MorphDB.Service.OData;

/// <summary>
/// The system query options (<c>$…</c>) a request may carry, held to what the service serves. An option that is
/// not served is refused rather than answered without: the answer to a narrowing option left out is every row, with
/// a <c>200</c>. A custom option (no <c>$</c>) and a parameter alias (<c>@name</c>) belong to the request and are
/// not this check's business. OData 4.0 Part 1 §6.1: a service fails a request carrying an unsupported option the
/// specification defines, and should fail one carrying an option it does not understand.
/// </summary>
internal static class ODataSystemOptions
{
    private static readonly string[] CollectionOptions = ["$filter", "$orderby", "$top", "$skip", "$select", "$count", "$format"];

    private static readonly string[] EntityOptions = ["$select", "$format"];

    private static readonly string Served = string.Join(", ", CollectionOptions);

    /// <summary>
    /// What is wrong with a request's options, or null when nothing is: the first option the service does not serve
    /// (on one entity, one that only a collection takes), or a value that is not one the option takes.
    /// </summary>
    /// <param name="query">The request's query options, by name.</param>
    /// <param name="entity">Whether the request reads one entity (its key in the path) rather than a collection.</param>
    public static string? Problem(IEnumerable<KeyValuePair<string, StringValues>> query, bool entity)
    {
        var served = entity ? EntityOptions : CollectionOptions;
        foreach (var (name, values) in query)
        {
            if (!name.StartsWith('$'))
            {
                continue;
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
        foreach (var (name, values) in query)
        {
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
