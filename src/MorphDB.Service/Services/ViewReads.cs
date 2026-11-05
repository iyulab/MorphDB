using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Services;

/// <summary>
/// Reading a view's rows by the view-data endpoint's rules, in one place: the REST endpoint and the MCP read tool both
/// build their request here, so what a person may select, filter and order a view by is the same on either door.
/// </summary>
internal static class ViewReads
{
    /// <summary>The request to <see cref="IViewManager.QueryViewAsync"/> for these inputs.</summary>
    /// <exception cref="ArgumentException">A condition the table read's own parser does not accept.</exception>
    internal static ViewQueryRequest Request(Guid projectId, string view, string? select, string[]? filter, string? orderBy, int? skip, int? take) => new()
    {
        ProjectId = projectId,
        ViewName = view,
        Columns = ParseColumns(select),
        Filters = ParseFilters(filter),
        OrderBy = ParseOrderBy(orderBy),
        Skip = skip,
        Take = take,
    };

    private static List<string>? ParseColumns(string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
        {
            return null;
        }

        return [.. select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>
    /// The query's conditions, read by the table read's own parser: one condition per <c>filter</c>
    /// parameter, a malformed one refused rather than dropped.
    /// </summary>
    private static List<ViewFilterSpec>? ParseFilters(string[]? filters)
    {
        if (!FilterExpressions.Any(filters))
        {
            return null;
        }

        return [.. filters!.Select(expression =>
        {
            var (column, op, value) = FilterExpressions.Parse(expression);
            return new ViewFilterSpec { Field = column, Operator = op, Value = value, LogicalOp = LogicalOperator.And };
        })];
    }

    private static List<ViewOrderSpec>? ParseOrderBy(string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return null;
        }

        var orders = new List<ViewOrderSpec>();
        foreach (var part in orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            orders.Add(colon > 0
                ? new ViewOrderSpec { Column = part[..colon], Descending = part[(colon + 1)..].Equals("desc", StringComparison.OrdinalIgnoreCase) }
                : new ViewOrderSpec { Column = part, Descending = false });
        }

        return orders.Count > 0 ? orders : null;
    }
}
