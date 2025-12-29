using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies version management for optimistic locking (_version column).
/// </summary>
public sealed class VersionApplier : ITransformer
{
    private static readonly string VersionColumn = SystemColumns.Version;

    public int Order => PipelineOrder.VersionApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyVersion
            && context.Table.VersioningEnabled
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        if (context.OperationType == WriteOperationType.Insert)
        {
            // Initialize version to 1 on insert
            context.Data[VersionColumn] = 1;
        }
        else if (context.OperationType is WriteOperationType.Update or WriteOperationType.Upsert)
        {
            // For update, check expected version if provided
            if (context.Options.ExpectedVersion.HasValue)
            {
                // Get current version from existing data
                var currentVersion = 0;
                if (context.ExistingData?.TryGetValue(VersionColumn, out var versionObj) == true)
                {
                    currentVersion = Convert.ToInt32(versionObj, System.Globalization.CultureInfo.InvariantCulture);
                }

                if (currentVersion != context.Options.ExpectedVersion.Value)
                {
                    ((WriteContext)context).AddError(
                        VersionColumn,
                        ValidationErrorCodes.VersionConflict,
                        $"Version conflict: expected {context.Options.ExpectedVersion.Value}, but current version is {currentVersion}.",
                        context.Options.ExpectedVersion.Value,
                        stopPipeline: true);
                    return Task.CompletedTask;
                }
            }

            // Increment version - this will be handled specially in the SQL execution
            // We mark it with a special value that the executor will interpret
            context.Data[VersionColumn] = new VersionIncrement();
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Marker class indicating version should be incremented in SQL.
/// </summary>
public sealed class VersionIncrement
{
    public static readonly VersionIncrement Instance = new();
}
