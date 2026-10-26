using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Mcp;
using MorphDB.Npgsql.Repositories;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Mcp;

/// <summary>
/// The MCP door's reads, made by the service's own read paths for the request that is being served: the project its
/// header names (checked as the REST API checks it) and, through the same services, the caller the authentication
/// middleware already established — so row-level security applies to a tool call exactly as it does to a REST one.
/// </summary>
internal sealed class ServiceReadPort(
    IProjectContextAccessor projectContext,
    IProjectRepository projects,
    ISchemaManager schemaManager,
    IMorphDataService dataService,
    IMetadataRepository metadataRepository,
    IAggregationService aggregationService) : IMorphReadPort
{
    public async ValueTask<IReadOnlyList<McpTableSummary>> ListTablesAsync(CancellationToken cancellationToken)
    {
        var projectId = await ProjectAsync(cancellationToken);
        var tables = await schemaManager.ListTablesAsync(projectId, cancellationToken);
        return [.. tables.Select(TableApiResponse.FromMetadata).Select(t => new McpTableSummary(t.Name, t.Columns.Count))];
    }

    public async ValueTask<McpTableDescription?> DescribeTableAsync(string table, CancellationToken cancellationToken)
    {
        var projectId = await ProjectAsync(cancellationToken);
        if (await schemaManager.GetTableAsync(projectId, table, cancellationToken) is not { } found)
        {
            return null;
        }

        var response = TableApiResponse.FromMetadata(found);
        return new McpTableDescription(response.Name, [.. response.Columns.Select(c => new McpColumn(c.Name, c.Type, !c.Nullable))]);
    }

    public async ValueTask<McpAggregation> AggregateAsync(McpAggregateQuery query, CancellationToken cancellationToken)
    {
        var projectId = await ProjectAsync(cancellationToken);
        if (query.Aggregates.Count == 0)
        {
            throw new MorphReadRefusedException("At least one aggregate is required.");
        }

        try
        {
            var request = new AggregationApiRequest
            {
                Aggregations = [.. query.Aggregates.Select(a => new AggregationColumnApiRequest { Function = a.Function, Column = a.Column, Alias = a.Alias })],
                GroupBy = query.GroupBy ?? [],
                Limit = query.MaxGroups,
            }.ToModel() with
            {
                Filter = query.Filter is { Count: > 0 }
                    ? [.. query.Filter.Select(e => FilterExpressions.Parse(e)).Select(c => new FilterCondition { Column = c.Column, Operator = c.Operator, Value = c.Value })]
                    : null,
            };
            var result = await aggregationService.AggregateAsync(projectId, query.Table, request, cancellationToken);
            return new McpAggregation(result.Data, result.TotalGroups, query.MaxGroups);
        }
        catch (Exception e) when (e is ArgumentException or NotFoundException)
        {
            throw new MorphReadRefusedException(e.Message, e);
        }
    }

    public async ValueTask<IDictionary<string, object?>?> GetRecordAsync(string table, string id, CancellationToken cancellationToken)
    {
        var projectId = await ProjectAsync(cancellationToken);
        if (!Guid.TryParse(id, out var recordId))
        {
            throw new MorphReadRefusedException($"'{id}' is not a record id; a record id is a GUID.");
        }

        try
        {
            return await dataService.GetByIdAsync(projectId, table, recordId, cancellationToken);
        }
        catch (NotFoundException e)
        {
            throw new MorphReadRefusedException(e.Message, e);
        }
    }

    public async ValueTask<McpRecordPage> QueryRecordsAsync(McpRecordQuery query, CancellationToken cancellationToken)
    {
        var projectId = await ProjectAsync(cancellationToken);
        try
        {
            var read = await DataReads.QueryAsync(
                dataService,
                metadataRepository,
                projectId,
                query.Table,
                new DataQueryParameters
                {
                    Select = query.Select,
                    Filter = query.Filter?.ToArray(),
                    OrderBy = query.OrderBy,
                    Search = query.Search,
                    Page = query.Page,
                    PageSize = query.PageSize,
                },
                cancellationToken);
            return new McpRecordPage(read.Rows, read.HasNext, read.Number, read.Size);
        }
        catch (Exception e) when (e is ArgumentException or NotFoundException)
        {
            throw new MorphReadRefusedException(e.Message, e);
        }
    }

    /// <summary>The project the request is scoped to, refused as the REST API refuses a request with none.</summary>
    private async ValueTask<Guid> ProjectAsync(CancellationToken cancellationToken)
    {
        var (projectId, refusal) = await ProjectScope.CheckAsync(projectContext, projects, cancellationToken);
        return projectId ?? throw new MorphReadRefusedException(refusal!.Exception.Message);
    }
}
