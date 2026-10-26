using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace MorphDB.Mcp;

/// <summary>
/// The tools of the door. Each reads and none writes; each runs as the caller the service authenticated for this
/// request, in the project its <c>X-Project-Id</c> header names — neither is an argument.
/// </summary>
[McpServerToolType]
internal sealed class MorphDbMcpTools(IMorphReadPort port, IOptions<MorphDbMcpOptions> options)
{
    /// <summary>The names the door offers, so a test can hold the list to this.</summary>
    internal static readonly IReadOnlyList<string> ToolNames = ["describe_tables", "describe_table", "query_records", "get_record", "aggregate"];

    [McpServerTool(Name = "describe_tables", Title = "List the project's tables", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the tables of the project this request is scoped to, with how many columns each declares. Read-only; runs as the authenticated caller.")]
    public async Task<McpTableList> DescribeTables(CancellationToken cancellationToken) =>
        new(await Guard(() => port.ListTablesAsync(cancellationToken)));

    [McpServerTool(Name = "describe_table", Title = "Describe a table", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Describes one table of the project: its columns, their data types and whether each is required. Read-only; runs as the authenticated caller.")]
    public async Task<McpTableDescription> DescribeTable(
        [Description("The table's name.")] string table,
        CancellationToken cancellationToken) =>
        await Guard(() => port.DescribeTableAsync(table, cancellationToken)) ?? throw new McpException($"The project has no table '{table}'.");

    [McpServerTool(Name = "query_records", Title = "Read a table's rows", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reads one page of a table's rows as the authenticated caller, who sees only the rows their row-level rules allow. Read-only. The page is capped; hasNext says whether more follow.")]
    public async Task<McpRecordPage> QueryRecords(
        [Description("The table's name.")] string table,
        [Description("Comma-separated columns to return; all when omitted.")] string? select = null,
        [Description("Conditions, each 'column:operator:value'; all must hold.")] string[]? filter = null,
        [Description("Columns to order by, each 'column:asc' or 'column:desc', comma-separated.")] string? orderBy = null,
        [Description("Text matched case-insensitively across the table's text columns.")] string? search = null,
        [Description("1-based page number.")] int page = 1,
        [Description("Rows per page; capped by the door.")] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        await Guard(() => port.QueryRecordsAsync(
            new McpRecordQuery(table, select, filter, orderBy, search, Math.Max(page, 1), Math.Clamp(pageSize, 1, Math.Max(options.Value.MaxPageSize, 1))),
            cancellationToken));

    [McpServerTool(Name = "get_record", Title = "Read one record", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reads one record by its id as the authenticated caller. A record their row-level rules do not let them see is reported as not found. Read-only.")]
    public async Task<McpRecord> GetRecord(
        [Description("The table's name.")] string table,
        [Description("The record's id (a GUID, the '_id' of a row).")] string id,
        CancellationToken cancellationToken = default) =>
        new(await Guard(() => port.GetRecordAsync(table, id, cancellationToken)) ?? throw new McpException($"The table '{table}' has no record '{id}' you can see."));

    [McpServerTool(Name = "aggregate", Title = "Count, sum or average a table's rows", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Aggregates a table's rows as the authenticated caller, who is counted over only the rows their row-level rules allow: count, countDistinct, sum, avg, min or max, optionally grouped. Read-only. The number of groups is capped.")]
    public async Task<McpAggregation> Aggregate(
        [Description("The table's name.")] string table,
        [Description("What to compute: objects with 'function' (count, countDistinct, sum, avg, min, max), 'column' (omit for a count of rows) and 'alias' (the result's name).")] McpAggregate[] aggregates,
        [Description("Columns to group by.")] string[]? groupBy = null,
        [Description("Conditions on the rows before grouping, each 'column:operator:value'; all must hold.")] string[]? filter = null,
        [Description("The most groups to return; capped by the door.")] int maxGroups = 100,
        CancellationToken cancellationToken = default) =>
        await Guard(() => port.AggregateAsync(
            new McpAggregateQuery(table, aggregates, groupBy, filter, Math.Clamp(maxGroups, 1, Math.Max(options.Value.MaxPageSize, 1))),
            cancellationToken));

    // A refusal the caller can act on is the tool's error, in the refusal's own words — anything else is not shown to them.
    private static async Task<T> Guard<T>(Func<ValueTask<T>> read)
    {
        try
        {
            return await read();
        }
        catch (MorphReadRefusedException e)
        {
            throw new McpException(e.Message, e);
        }
    }
}
