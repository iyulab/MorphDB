using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using Npgsql;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies sort order (_sort_order) for hierarchy-enabled tables.
/// Auto-generates the next sort order value if not provided.
/// </summary>
public sealed class SortOrderApplier : ITransformer
{
    private readonly NpgsqlDataSource _dataSource;

    private static readonly string SortOrderColumn = SystemColumns.SortOrder;
    private static readonly string ParentIdColumn = SystemColumns.ParentId;

    public SortOrderApplier(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public int Order => PipelineOrder.SortOrderApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplySortOrder
            && context.Table.HierarchyEnabled
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert;
    }

    public async Task ExecuteAsync(IWriteContext context)
    {
        // Skip if sort order is already provided
        if (context.Data.TryGetValue(SortOrderColumn, out var existingSortOrder) && existingSortOrder is not null)
        {
            return;
        }

        // Get parent_id (can be null for root level items)
        context.Data.TryGetValue(ParentIdColumn, out var parentIdValue);

        // Calculate next sort order for this parent
        var nextSortOrder = await GetNextSortOrderAsync(
            context.Table.PhysicalName,
            parentIdValue,
            context.CancellationToken);

        context.Data[SortOrderColumn] = nextSortOrder;
    }

    private async Task<int> GetNextSortOrderAsync(
        string tableName,
        object? parentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        string sql;
        object? param;

        if (parentId is null)
        {
            // Root level: find max sort_order where parent_id IS NULL
            sql = $"SELECT COALESCE(MAX({SortOrderColumn}), 0) + 1 FROM {tableName} WHERE {ParentIdColumn} IS NULL";
            param = null;
        }
        else
        {
            // Child level: find max sort_order for this parent
            sql = $"SELECT COALESCE(MAX({SortOrderColumn}), 0) + 1 FROM {tableName} WHERE {ParentIdColumn} = @parentId";
            param = new { parentId };
        }

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken));
    }
}
