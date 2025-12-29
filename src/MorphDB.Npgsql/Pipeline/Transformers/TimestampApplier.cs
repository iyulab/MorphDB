using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies automatic timestamps (_created_at, _updated_at).
/// </summary>
public sealed class TimestampApplier : ITransformer
{
    private const string CreatedAtColumn = "_created_at";
    private const string UpdatedAtColumn = "_updated_at";

    public int Order => PipelineOrder.TimestampApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyTimestamps
            && context.Table.TimestampsEnabled
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var now = DateTimeOffset.UtcNow;

        // Set _created_at on insert only
        if (context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert)
        {
            if (!context.Data.ContainsKey(CreatedAtColumn))
            {
                context.Data[CreatedAtColumn] = now;
            }
        }

        // Set _updated_at on insert and update
        context.Data[UpdatedAtColumn] = now;

        return Task.CompletedTask;
    }
}
