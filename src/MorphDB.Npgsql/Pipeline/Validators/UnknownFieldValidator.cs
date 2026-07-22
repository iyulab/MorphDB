using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Rejects a write that names a column the table does not declare.
/// <para>
/// Before this validator the executor silently skipped unknown keys "for resilience" — a caller's
/// typo answered 201 while its value vanished, discovered only later when a query came back empty
/// and the original was gone. That is the exact silence the constitution forbids at a boundary:
/// process exactly, or fail explicitly. The executor's skip still exists, but only behind the
/// explicit <see cref="WriteOptions.AllowUnknownFields"/> opt-in — consent is what turns a drop
/// from data loss into a feature.
/// </para>
/// <para>
/// The system namespace (<c>_</c>-prefixed and <c>project_id</c>) passes: the pipeline's own
/// transformers inject those keys after user input, and they are not the caller's vocabulary.
/// </para>
/// </summary>
public sealed class UnknownFieldValidator : IValidator
{
    public int Order => PipelineOrder.UnknownFieldValidator;

    public bool ShouldExecute(IWriteContext context)
    {
        return !context.Options.AllowUnknownFields
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var declared = context.Table.Columns
            .Where(c => c.IsActive)
            .Select(c => c.LogicalName)
            .ToHashSet(StringComparer.Ordinal);

        List<string>? unknown = null;
        foreach (var key in context.Data.Keys)
        {
            if (declared.Contains(key) || SystemColumns.IsSystemColumn(key))
            {
                continue;
            }

            (unknown ??= []).Add(key);
        }

        if (unknown is null)
        {
            return Task.CompletedTask;
        }

        // project_id is internal — naming it here would advertise a column no caller may use.
        var available = string.Join(", ", declared
            .Where(n => n != SystemColumns.ProjectId)
            .OrderBy(n => n, StringComparer.Ordinal));
        foreach (var key in unknown)
        {
            ((WriteContext)context).AddError(
                key,
                ValidationErrorCodes.UnknownColumn,
                $"Unknown column '{key}' on table '{context.Table.LogicalName}'. Available columns: {available}.",
                context.Data[key]);
        }

        return Task.CompletedTask;
    }
}
