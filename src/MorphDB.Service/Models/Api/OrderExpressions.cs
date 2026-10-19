using MorphDB.Core.Abstractions;

namespace MorphDB.Service.Models.Api;

/// <summary>
/// The <c>column</c> / <c>column:asc</c> / <c>column:desc</c> ordering language, read the same way by the
/// REST list, <c>POST /api/data/{table}/query</c> and GraphQL <c>records</c>.
/// <para>
/// Several keys are comma-separated and apply in the order written. A direction other than
/// <c>asc</c> or <c>desc</c> is refused rather than read as ascending: an order the server quietly
/// changes is a different answer to the same question.
/// </para>
/// </summary>
internal static class OrderExpressions
{
    public static IMorphQuery Apply(IMorphQuery query, string? expression)
    {
        foreach (var (column, descending) in Parse(expression))
        {
            query = descending ? query.OrderByDesc(column) : query.OrderBy(column);
        }

        return query;
    }

    public static IReadOnlyList<(string Column, bool Descending)> Parse(string? expression)
    {
        var keys = new List<(string, bool)>();
        foreach (var key in (expression ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = key.Split(':', 2);
            var column = parts[0].Trim();
            var direction = parts.Length > 1 ? parts[1].Trim() : "asc";

            if (column.Length == 0)
            {
                throw new ArgumentException($"Invalid order '{key}': name a column, as 'column' or 'column:desc'.");
            }

            keys.Add((column, IsDescending(direction, key)));
        }

        return keys;
    }

    /// <summary>
    /// A direction as written: <c>asc</c> (or none) or <c>desc</c>, in any case. Anything else is refused.
    /// </summary>
    public static bool IsDescending(string? direction, string? context = null)
    {
        var written = direction?.Trim() ?? "";
        return written.ToLowerInvariant() switch
        {
            "" or "asc" => false,
            "desc" => true,
            _ => throw new ArgumentException(
                $"Invalid order{(context is null ? "" : $" '{context}'")}: the direction is 'asc' or 'desc', not '{written}'."),
        };
    }
}
