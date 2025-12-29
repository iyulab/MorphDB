using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates that required fields have values.
/// This is the virtual NOT NULL constraint - enforced at application layer.
/// </summary>
public sealed class RequiredValidator : IValidator
{
    public int Order => PipelineOrder.RequiredValidator;

    public bool ShouldExecute(IWriteContext context)
    {
        // Only validate on insert/update, and if enabled in options
        return context.Options.ValidateRequired
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var requiredColumns = context.Table.Columns
            .Where(c => c.IsRequired && !c.IsPrimaryKey && c.IsActive)
            .ToList();

        foreach (var column in requiredColumns)
        {
            // For updates, only check if the field is being updated
            if (context.OperationType == WriteOperationType.Update)
            {
                if (!context.Data.ContainsKey(column.LogicalName))
                {
                    // Field not in update payload, skip (existing value is preserved)
                    continue;
                }
            }

            var hasValue = context.Data.TryGetValue(column.LogicalName, out var value);

            // Check if value is null or missing
            if (!hasValue || value is null)
            {
                // Check if there's a default value that will be applied
                if (column.DefaultType != DefaultValueType.None && column.DefaultValue is not null)
                {
                    // Default will be applied by transformer, skip error
                    continue;
                }

                ((WriteContext)context).AddError(
                    column.LogicalName,
                    ValidationErrorCodes.Required,
                    $"Field '{column.LogicalName}' is required and cannot be null.",
                    value);
            }
            // Check for empty string if it's a text field
            else if (value is string str && string.IsNullOrWhiteSpace(str))
            {
                // Optional: treat empty strings as null for required fields
                // Uncomment if needed:
                // ((WriteContext)context).AddError(
                //     column.LogicalName,
                //     ValidationErrorCodes.Required,
                //     $"Field '{column.LogicalName}' is required and cannot be empty.",
                //     value);
            }
        }

        return Task.CompletedTask;
    }
}
