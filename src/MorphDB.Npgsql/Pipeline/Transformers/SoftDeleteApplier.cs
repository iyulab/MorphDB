using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Transforms DELETE operations into soft-delete updates.
/// Sets _deleted_at instead of physically removing the row.
/// </summary>
public sealed class SoftDeleteApplier : ITransformer
{
    private const string DeletedAtColumn = "_deleted_at";
    private const string DeletedByColumn = "_deleted_by";

    // Run early in delete operations
    public int Order => 50;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.OperationType == WriteOperationType.Delete
            && context.Table.SoftDeleteEnabled;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        // Set the deleted timestamp
        context.Data[DeletedAtColumn] = DateTimeOffset.UtcNow;

        // Set deleted_by if audit fields are enabled and user is known
        if (context.Table.AuditFieldsEnabled && context.SecurityContext?.UserId is not null)
        {
            context.Data[DeletedByColumn] = context.SecurityContext.UserId;
        }

        return Task.CompletedTask;
    }
}
