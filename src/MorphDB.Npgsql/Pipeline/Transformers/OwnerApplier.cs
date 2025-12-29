using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies owner ID (_owner_id) from security context for ownership-enabled tables.
/// Sets _owner_id on insert to the current user's ID.
/// </summary>
public sealed class OwnerApplier : ITransformer
{
    private static readonly string OwnerIdColumn = SystemColumns.OwnerId;

    public int Order => PipelineOrder.OwnerApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyOwnership
            && context.Table.OwnershipEnabled
            && context.SecurityContext is not null
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var userId = context.SecurityContext?.UserId;
        if (userId is null) return Task.CompletedTask;

        // Set _owner_id on insert only (owner doesn't change on updates)
        // User can explicitly provide _owner_id to override
        if (!context.Data.ContainsKey(OwnerIdColumn))
        {
            context.Data[OwnerIdColumn] = userId;
        }

        return Task.CompletedTask;
    }
}
