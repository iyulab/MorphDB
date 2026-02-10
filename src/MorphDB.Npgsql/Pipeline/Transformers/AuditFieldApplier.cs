using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies audit fields (_created_by, _updated_by) from security context.
/// </summary>
public sealed class AuditFieldApplier : ITransformer
{
    private static readonly string CreatedByColumn = SystemColumns.CreatedBy;
    private static readonly string UpdatedByColumn = SystemColumns.UpdatedBy;

    public int Order => PipelineOrder.AuditFieldApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyAuditFields
            && context.Table.AuditFieldsEnabled
            && context.SecurityContext is not null
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var userId = context.SecurityContext?.UserId;
        if (userId is null)
            return Task.CompletedTask;

        // Set _created_by on insert only
        if (context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert)
        {
            if (!context.Data.ContainsKey(CreatedByColumn))
            {
                context.Data[CreatedByColumn] = userId;
            }
        }

        // Set _updated_by on insert and update
        context.Data[UpdatedByColumn] = userId;

        return Task.CompletedTask;
    }
}
