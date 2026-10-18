using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies sort order (_sort_order) for hierarchy-enabled tables.
/// Auto-generates the next sort order value if not provided.
/// </summary>
public sealed class SortOrderApplier : ITransformer
{
    private readonly MorphConnectionFactory _connections;

    private static readonly string SortOrderColumn = SystemColumns.SortOrder;
    private static readonly string ParentIdColumn = SystemColumns.ParentId;

    public SortOrderApplier(MorphConnectionFactory connections)
    {
        _connections = connections;
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

        // Get parent_id (can be null for root level items). The REST path delivers it as a
        // JsonElement, which Dapper binds as jsonb -- and `uuid = jsonb` is no operator PostgreSQL
        // has, so every child inserted over HTTP failed with a 500 before it was written. Convert it
        // to the column's DB value first, as the foreign-key and unique validators do. A value that
        // is not a uuid at all is left for TypeValidator to refuse; this step only numbers rows.
        context.Data.TryGetValue(ParentIdColumn, out var parentIdValue);
        object? parentId;
        try
        {
            parentId = TypeMapper.ToDbValue(parentIdValue, MorphDataType.Uuid);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return;
        }

        // Calculate next sort order for this parent
        var nextSortOrder = await GetNextSortOrderAsync(
            context.Table.PhysicalName,
            parentId,
            context.CancellationToken);

        context.Data[SortOrderColumn] = nextSortOrder;
    }

    private async Task<int> GetNextSortOrderAsync(
        string tableName,
        object? parentId,
        CancellationToken cancellationToken)
    {
        await using var conn = ConnectionScope.HasScope
            ? new ScopedConnection(ConnectionScope.CurrentConnection!, ConnectionScope.CurrentTransaction, false)
            : new ScopedConnection(await _connections.OpenAsync(cancellationToken), null, true);

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

        return await conn.Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, param, transaction: conn.Transaction, cancellationToken: cancellationToken));
    }
}
