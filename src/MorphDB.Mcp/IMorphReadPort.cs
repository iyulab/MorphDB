namespace MorphDB.Mcp;

/// <summary>
/// What the MCP tools may ask of the service, and all they may ask: the service implements it over the read paths its
/// REST API already uses, for the caller and project the request was authenticated and scoped to. It has no member
/// that writes, so a tool cannot either.
/// </summary>
public interface IMorphReadPort
{
    /// <summary>The tables of the request's project.</summary>
    /// <exception cref="MorphReadRefusedException">The request names no usable project.</exception>
    ValueTask<IReadOnlyList<McpTableSummary>> ListTablesAsync(CancellationToken cancellationToken);

    /// <summary>One table's columns, or <see langword="null"/> when the project has no such table.</summary>
    /// <exception cref="MorphReadRefusedException">The request names no usable project.</exception>
    ValueTask<McpTableDescription?> DescribeTableAsync(string table, CancellationToken cancellationToken);

    /// <summary>One record by its id, or <see langword="null"/> when there is none the caller may see.</summary>
    /// <exception cref="MorphReadRefusedException">The request names no usable project, the table is unknown, or the id is not one.</exception>
    ValueTask<IDictionary<string, object?>?> GetRecordAsync(string table, string id, CancellationToken cancellationToken);

    /// <summary>Aggregates (count, sum, average, minimum, maximum) over a table's rows, under the caller's own row-level rules.</summary>
    /// <exception cref="MorphReadRefusedException">The request names no usable project, the table or a column is unknown, or an aggregate, grouping or filter is not accepted.</exception>
    ValueTask<McpAggregation> AggregateAsync(McpAggregateQuery query, CancellationToken cancellationToken);

    /// <summary>One page of a table's rows, under the caller's own row-level rules.</summary>
    /// <exception cref="MorphReadRefusedException">The request names no usable project, the table is unknown, or the filter, ordering or search is not accepted.</exception>
    ValueTask<McpRecordPage> QueryRecordsAsync(McpRecordQuery query, CancellationToken cancellationToken);
}

/// <summary>The read cannot be made as asked; the message says why, in words the caller can act on.</summary>
public sealed class MorphReadRefusedException : Exception
{
    /// <summary>Creates the refusal.</summary>
    public MorphReadRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the refusal, with the exception it follows from.</summary>
    public MorphReadRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A table in the project.</summary>
/// <param name="Name">The table's name.</param>
/// <param name="Columns">How many columns it declares.</param>
public sealed record McpTableSummary(string Name, int Columns);

/// <summary>A column of a table.</summary>
/// <param name="Name">The column's name.</param>
/// <param name="Type">Its data type.</param>
/// <param name="Required">Whether a record must give it a value.</param>
public sealed record McpColumn(string Name, string Type, bool Required);

/// <summary>A table and its columns.</summary>
/// <param name="Name">The table's name.</param>
/// <param name="Columns">Its columns.</param>
public sealed record McpTableDescription(string Name, IReadOnlyList<McpColumn> Columns);

/// <summary>A read of one page of a table, by the list endpoint's rules.</summary>
/// <param name="Table">The table.</param>
/// <param name="Select">Comma-separated columns to return; all when empty.</param>
/// <param name="Filter">Conditions, one <c>column:op:value</c> each; all apply.</param>
/// <param name="OrderBy">Columns to order by, <c>column:asc</c> or <c>column:desc</c>.</param>
/// <param name="Search">Case-insensitive text matched across the table's text columns.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Rows per page; the door caps it.</param>
public sealed record McpRecordQuery(string Table, string? Select, IReadOnlyList<string>? Filter, string? OrderBy, string? Search, int Page, int PageSize);

/// <summary>One page of rows.</summary>
/// <param name="Rows">The rows, each by column name.</param>
/// <param name="HasNext">Whether another page follows.</param>
/// <param name="Page">The page number.</param>
/// <param name="PageSize">The page size applied.</param>
public sealed record McpRecordPage(IReadOnlyList<IDictionary<string, object?>> Rows, bool HasNext, int Page, int PageSize);

/// <summary>The tables of a project, as an object — a tool's structured result is one.</summary>
/// <param name="Tables">The tables.</param>
public sealed record McpTableList(IReadOnlyList<McpTableSummary> Tables);

/// <summary>One record, by column name.</summary>
/// <param name="Record">The record's columns and values.</param>
public sealed record McpRecord(IDictionary<string, object?> Record);

/// <summary>One aggregate to compute.</summary>
/// <param name="Function">count, countDistinct, sum, avg, min or max.</param>
/// <param name="Column">The column to aggregate; none for a count of rows.</param>
/// <param name="Alias">The name the result carries.</param>
public sealed record McpAggregate(string Function, string? Column, string Alias);

/// <summary>An aggregation of a table's rows.</summary>
/// <param name="Table">The table.</param>
/// <param name="Aggregates">What to compute.</param>
/// <param name="GroupBy">Columns to group by; one row of results overall when none.</param>
/// <param name="Filter">Conditions on the rows, each <c>column:op:value</c>; all apply before grouping.</param>
/// <param name="MaxGroups">The most groups to return; the door caps it.</param>
public sealed record McpAggregateQuery(string Table, IReadOnlyList<McpAggregate> Aggregates, IReadOnlyList<string>? GroupBy, IReadOnlyList<string>? Filter, int MaxGroups);

/// <summary>The result of an aggregation.</summary>
/// <param name="Groups">One row per group: the grouping columns' values and the aggregates under their aliases.</param>
/// <param name="TotalGroups">How many groups there are before the cap, when known.</param>
/// <param name="MaxGroups">The cap applied.</param>
public sealed record McpAggregation(IReadOnlyList<IDictionary<string, object?>> Groups, long? TotalGroups, int MaxGroups);
