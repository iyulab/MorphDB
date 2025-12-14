using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.OData.Edm;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;

namespace MorphDB.Service.OData;

/// <summary>
/// Handles OData query options and converts them to MorphDB queries.
/// </summary>
public sealed partial class ODataQueryHandler
{
    private readonly ISchemaManager _schemaManager;
    private readonly IMorphDataService _dataService;

    public ODataQueryHandler(ISchemaManager schemaManager, IMorphDataService dataService)
    {
        _schemaManager = schemaManager;
        _dataService = dataService;
    }

    /// <summary>
    /// Executes an OData query and returns the results.
    /// </summary>
    public async Task<ODataQueryResult> ExecuteQueryAsync(
        Guid tenantId,
        string entitySetName,
        IEdmModel model,
        ODataQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        // Find the table by entity set name (PascalCase to logical name)
        var tableName = ToLogicalName(entitySetName);
        var table = await _schemaManager.GetTableAsync(tenantId, tableName, cancellationToken);
        if (table == null)
        {
            throw new InvalidOperationException($"Entity set '{entitySetName}' not found.");
        }

        var query = _dataService.Query(tenantId).From(tableName);

        // Apply $select
        if (!string.IsNullOrEmpty(options.Select))
        {
            var columns = options.Select.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToArray();
            query = query.SelectColumns(columns);
        }
        else
        {
            query = query.SelectAll();
        }

        // Apply $filter
        if (!string.IsNullOrEmpty(options.Filter))
        {
            query = ApplyFilter(query, options.Filter, table);
        }

        // Apply $orderby
        if (!string.IsNullOrEmpty(options.OrderBy))
        {
            query = ApplyOrderBy(query, options.OrderBy);
        }

        // Get total count if requested
        long? totalCount = null;
        if (options.Count)
        {
            totalCount = await _dataService.Query(tenantId)
                .From(tableName)
                .SelectAll()
                .CountAsync(cancellationToken);
        }

        // Apply $skip
        if (options.Skip > 0)
        {
            query = query.Offset(options.Skip);
        }

        // Apply $top
        if (options.Top > 0)
        {
            query = query.Limit(options.Top);
        }
        else
        {
            // Default page size
            query = query.Limit(100);
        }

        var records = await query.ToListAsync(cancellationToken);

        return new ODataQueryResult
        {
            Records = records,
            TotalCount = totalCount,
            EntitySetName = entitySetName,
            TableMetadata = table
        };
    }

    /// <summary>
    /// Gets a single entity by ID.
    /// </summary>
    public async Task<IDictionary<string, object?>?> GetByIdAsync(
        Guid tenantId,
        string entitySetName,
        Guid id,
        ODataQueryOptions options,
        CancellationToken cancellationToken = default)
    {
        var tableName = ToLogicalName(entitySetName);
        var record = await _dataService.GetByIdAsync(tenantId, tableName, id, cancellationToken);

        if (record == null)
            return null;

        // Apply $select if specified
        if (!string.IsNullOrEmpty(options.Select))
        {
            var columns = options.Select.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToLowerInvariant())
                .ToHashSet();

            // Always include id
            columns.Add("id");

            var filtered = new Dictionary<string, object?>();
            foreach (var kvp in record)
            {
                if (columns.Contains(kvp.Key.ToLowerInvariant()))
                {
                    filtered[kvp.Key] = kvp.Value;
                }
            }
            return filtered;
        }

        return record;
    }

    private static IMorphQuery ApplyFilter(IMorphQuery query, string filter, TableMetadata table)
    {
        // Parse simple OData filter expressions
        // Supports: eq, ne, gt, ge, lt, le, contains, startswith, endswith
        // Examples: name eq 'John', age gt 18, contains(name, 'test')

        var match = SimpleFilterRegex().Match(filter);
        if (match.Success)
        {
            var column = match.Groups["column"].Value;
            var op = match.Groups["op"].Value.ToLowerInvariant();
            var value = ParseValue(match.Groups["value"].Value);

            return op switch
            {
                "eq" => query.Where(column, FilterOperator.Equals, value),
                "ne" => query.Where(column, FilterOperator.NotEquals, value),
                "gt" => query.Where(column, FilterOperator.GreaterThan, value),
                "ge" => query.Where(column, FilterOperator.GreaterThanOrEquals, value),
                "lt" => query.Where(column, FilterOperator.LessThan, value),
                "le" => query.Where(column, FilterOperator.LessThanOrEquals, value),
                _ => query
            };
        }

        // Check for function-based filters
        var containsMatch = ContainsFilterRegex().Match(filter);
        if (containsMatch.Success)
        {
            var column = containsMatch.Groups["column"].Value;
            var value = containsMatch.Groups["value"].Value.Trim('\'');
            return query.Where(column, FilterOperator.Contains, value);
        }

        var startsWithMatch = StartsWithFilterRegex().Match(filter);
        if (startsWithMatch.Success)
        {
            var column = startsWithMatch.Groups["column"].Value;
            var value = startsWithMatch.Groups["value"].Value.Trim('\'');
            return query.Where(column, FilterOperator.StartsWith, value);
        }

        var endsWithMatch = EndsWithFilterRegex().Match(filter);
        if (endsWithMatch.Success)
        {
            var column = endsWithMatch.Groups["column"].Value;
            var value = endsWithMatch.Groups["value"].Value.Trim('\'');
            return query.Where(column, FilterOperator.EndsWith, value);
        }

        // If we can't parse the filter, just return the query as-is
        return query;
    }

    private static IMorphQuery ApplyOrderBy(IMorphQuery query, string orderBy)
    {
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var tokens = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var column = tokens[0];
            var isDescending = tokens.Length > 1 && tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            query = isDescending ? query.OrderByDesc(column) : query.OrderBy(column);
        }
        return query;
    }

    private static object? ParseValue(string value)
    {
        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1];
        }

        if (value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decValue))
        {
            return decValue;
        }

        if (Guid.TryParse(value, out var guidValue))
        {
            return guidValue;
        }

        return value;
    }

    private static string ToLogicalName(string entitySetName)
    {
        // Convert PascalCase to snake_case
        return string.Concat(entitySetName.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    [GeneratedRegex(@"^(?<column>\w+)\s+(?<op>eq|ne|gt|ge|lt|le)\s+(?<value>'[^']*'|\d+\.?\d*|true|false|null|[0-9a-f-]{36})$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleFilterRegex();

    [GeneratedRegex(@"^contains\((?<column>\w+),\s*(?<value>'[^']*')\)$", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsFilterRegex();

    [GeneratedRegex(@"^startswith\((?<column>\w+),\s*(?<value>'[^']*')\)$", RegexOptions.IgnoreCase)]
    private static partial Regex StartsWithFilterRegex();

    [GeneratedRegex(@"^endswith\((?<column>\w+),\s*(?<value>'[^']*')\)$", RegexOptions.IgnoreCase)]
    private static partial Regex EndsWithFilterRegex();
}

/// <summary>
/// OData query options parsed from the request.
/// </summary>
public sealed class ODataQueryOptions
{
    public string? Filter { get; init; }
    public string? OrderBy { get; init; }
    public string? Select { get; init; }
    public string? Expand { get; init; }
    public int Top { get; init; }
    public int Skip { get; init; }
    public bool Count { get; init; }
}

/// <summary>
/// Result of an OData query execution.
/// </summary>
public sealed class ODataQueryResult
{
    public required IReadOnlyList<IDictionary<string, object?>> Records { get; init; }
    public long? TotalCount { get; init; }
    public required string EntitySetName { get; init; }
    public required TableMetadata TableMetadata { get; init; }
}
