using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies UUID v7 primary key for new records.
/// UUID v7 is time-sortable, providing better index performance than random UUIDs.
/// </summary>
public sealed class IdApplier : ITransformer
{
    private static readonly string IdColumn = SystemColumns.Id;

    // Run first in the pipeline
    public int Order => PipelineOrder.IdApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        // Only apply ID on insert operations
        return context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        // Only set if not already provided
        if (!context.Data.ContainsKey(IdColumn))
        {
            // Generate UUID v7 (time-sortable UUID)
            // .NET 9+ provides native Guid.CreateVersion7()
            context.Data[IdColumn] = Guid.CreateVersion7();
        }

        return Task.CompletedTask;
    }
}
