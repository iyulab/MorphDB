using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Infrastructure;
using MorphDB.Npgsql.Pipeline.Transformers;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Checks that every value in a write can be read as its column's type, before anything is written.
/// The write executor converts each value the same way when it binds it; a value that did not
/// convert failed there, as an unhandled exception and so a <c>500</c>, for any column a request
/// could name — an id column given a word, a number column given text. Here the same conversion
/// becomes a validation error naming the column, reported together with every other one.
/// </summary>
public sealed class TypeValidator : IValidator
{
    public int Order => PipelineOrder.TypeValidator;

    public bool ShouldExecute(IWriteContext context) =>
        context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;

    public Task ExecuteAsync(IWriteContext context)
    {
        var columns = context.Table.Columns.ToDictionary(c => c.LogicalName, StringComparer.Ordinal);
        foreach (var (name, value) in context.Data)
        {
            if (value is null or VersionIncrement || !columns.TryGetValue(name, out var column))
            {
                // Nothing to convert, a marker the executor handles itself, or a name the unknown-field
                // validator has already judged.
                continue;
            }

            try
            {
                _ = TypeMapper.ToDbValue(value, column.DataType);
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or InvalidCastException or OverflowException)
            {
                ((WriteContext)context).AddError(
                    name,
                    ValidationErrorCodes.TypeMismatch,
                    $"Field '{name}' holds {column.DataType.ToString().ToLowerInvariant()} values; the value given cannot be read as one.",
                    value);
            }
        }

        return Task.CompletedTask;
    }
}
