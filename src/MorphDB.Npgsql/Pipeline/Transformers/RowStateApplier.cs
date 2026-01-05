using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies row state (_row_state) for draft mode operations.
/// Sets _row_state = 'draft' when SaveAsDraft option is enabled.
/// </summary>
public sealed class RowStateApplier : ITransformer
{
    public int Order => PipelineOrder.RowStateApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        // Only execute for tables with RowStateEnabled
        // and on Insert/Update/Upsert operations
        return context.Table.RowStateEnabled
            && context.OperationType is WriteOperationType.Insert
                or WriteOperationType.Update
                or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        if (context.Options.SaveAsDraft)
        {
            // Set row state to draft for deferred validation
            context.Data[SystemColumns.RowState] = RowStateValue.Draft.ToString().ToLowerInvariant();

            // Clear any previous errors
            if (context.Data.ContainsKey(SystemColumns.RowErrors))
            {
                context.Data[SystemColumns.RowErrors] = null;
            }
        }
        else if (context.OperationType == WriteOperationType.Insert)
        {
            // For normal inserts, default to 'valid' if not in draft mode
            if (!context.Data.ContainsKey(SystemColumns.RowState))
            {
                context.Data[SystemColumns.RowState] = RowStateValue.Valid.ToString().ToLowerInvariant();
            }
        }

        return Task.CompletedTask;
    }
}
