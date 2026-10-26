using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Models.Api;

using static MorphDB.Core.Abstractions.QueryLimits;

namespace MorphDB.Service.Services;

/// <summary>
/// Reading a table's rows by the list endpoint's rules, in one place: the REST list and the MCP read tool both ask
/// here, so what a person may filter, order, search and page by is the same on either door.
/// </summary>
internal static class DataReads
{
    /// <summary>One page of a table's rows, and what is needed to say whether more follow.</summary>
    internal sealed record Page(IReadOnlyList<IDictionary<string, object?>> Rows, bool HasNext, long? TotalCount, int Number, int Size);

    /// <exception cref="ArgumentException">A filter, ordering, search or state the rules do not accept.</exception>
    internal static async Task<Page> QueryAsync(
        IMorphDataService dataService,
        IMetadataRepository metadataRepository,
        Guid projectId,
        string table,
        DataQueryParameters query,
        CancellationToken cancellationToken)
    {
        // Validate pagination
        var pageSize = Math.Clamp(query.PageSize, 1, Math.Min(1000, MaxPageSize));
        var page = Math.Max(query.Page, 1);
        var skip = PaginationInfo.Skip(page, pageSize, query.Offset);

        // Build query
        var morphQuery = dataService.Query(projectId).From(table);
        if (query.IncludeDeleted)
        {
            morphQuery = morphQuery.IncludeDeleted();
        }

        // Select columns
        if (!string.IsNullOrEmpty(query.Select))
        {
            var columns = query.Select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            morphQuery = morphQuery.SelectColumns([.. SystemColumns.WithRecordId(columns)]);
        }
        else
        {
            morphQuery = morphQuery.SelectAll();
        }

        // Apply filters
        morphQuery = FilterExpressions.Apply(morphQuery, query.Filter);

        // Apply search across text columns
        if (!string.IsNullOrEmpty(query.Search))
        {
            var tableMetadata = await metadataRepository.GetTableByNameAsync(
                projectId, table, includeColumns: true, cancellationToken);

            if (tableMetadata is not null)
            {
                morphQuery = ApplySearch(morphQuery, query.Search, tableMetadata.Columns);
            }
        }

        // Apply row state filter if specified
        morphQuery = ApplyRowStateFilter(morphQuery, query.State);

        // Apply ordering
        if (!string.IsNullOrEmpty(query.OrderBy))
        {
            morphQuery = OrderExpressions.Apply(morphQuery, query.OrderBy);
        }

        // Count only when asked; the page is read one row past its size to know whether more follow
        long? totalCount = query.Count ? await morphQuery.CountAsync(cancellationToken) : null;
        var (results, more) = PaginationInfo.Probe(await morphQuery.Limit(pageSize + 1).Offset(skip).ToListAsync(cancellationToken), pageSize);

        return new Page(results, more, totalCount, query.Offset is null ? page : PaginationInfo.PageOf(skip, pageSize), pageSize);
    }

    /// <summary>
    /// Determines if a MorphDataType is a text-like type suitable for ILIKE search.
    /// </summary>
    private static bool IsTextSearchable(MorphDataType dataType) => dataType is
        MorphDataType.Text or
        MorphDataType.LongText or
        MorphDataType.Email or
        MorphDataType.Url or
        MorphDataType.Phone or
        MorphDataType.SingleSelect;

    private static IMorphQuery ApplySearch(
        IMorphQuery query,
        string searchText,
        IReadOnlyList<ColumnMetadata> columns)
    {
        var textColumns = columns
            .Where(c => !c.IsSystemColumn && !c.IsDerived && IsTextSearchable(c.DataType))
            .ToList();

        if (textColumns.Count == 0)
            return query;

        var searchPattern = $"%{searchText}%";
        var isFirst = true;

        foreach (var column in textColumns)
        {
            if (isFirst)
            {
                query = query.Where(column.LogicalName, FilterOperator.ILike, searchPattern);
                isFirst = false;
            }
            else
            {
                query = query.OrWhere(column.LogicalName, FilterOperator.ILike, searchPattern);
            }
        }

        return query;
    }

    private static IMorphQuery ApplyRowStateFilter(IMorphQuery query, string? state)
    {
        // If state is not specified, don't apply any filter (backward compatible).
        // This allows queries on tables without RowStateEnabled to work normally.
        if (string.IsNullOrWhiteSpace(state))
        {
            return query;
        }

        // Normalize state to lowercase
        var normalizedState = state.Trim().ToLowerInvariant();

        return normalizedState switch
        {
            // "all" returns all records regardless of _row_state
            "all" => query,

            // "draft" returns only draft records
            "draft" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Draft.ToString().ToLowerInvariant()),

            // "error" returns only error records
            "error" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Error.ToString().ToLowerInvariant()),

            // "valid" returns valid records only
            "valid" => query.Where(SystemColumns.RowState, FilterOperator.Equals, RowStateValue.Valid.ToString().ToLowerInvariant()),

            // An unknown state used to apply no filter at all — an answer wider than the question.
            _ => throw new ArgumentException($"Unknown row state '{state}'. Supported: valid, draft, error, all.")
        };
    }
}
