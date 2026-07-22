using MorphDB.Core.Models;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The canonical physical-row → logical-dictionary mapping (previously four identical private
/// copies across the data service, write executor, query builder and transaction service).
/// <c>project_id</c> never crosses this boundary: the project is an internal operating unit, every
/// request is already project-scoped, and the GUID carries zero information for a consumer —
/// excluding it here covers every read and write-returning surface at once (REST, GraphQL, OData,
/// views, transactions, batch).
/// </summary>
internal static class RowMapper
{
    public static Dictionary<string, object?> MapToLogicalDictionary(
        dynamic row,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var physicalToLogical = columns.ToDictionary(c => c.PhysicalName.ToLowerInvariant(), c => c);
        var result = new Dictionary<string, object?>();

        var rowDict = (IDictionary<string, object?>)row;

        foreach (var (key, value) in rowDict)
        {
            var normalizedKey = key.ToLowerInvariant();
            if (normalizedKey == SystemColumns.ProjectId)
            {
                continue;
            }

            if (physicalToLogical.TryGetValue(normalizedKey, out var column))
            {
                var convertedValue = TypeMapper.FromDbValue(value, column.DataType);
                result[column.LogicalName] = convertedValue;
            }
            else
            {
                // Unknown column (e.g., an aggregate alias), keep as-is
                result[key] = value;
            }
        }

        return result;
    }
}
