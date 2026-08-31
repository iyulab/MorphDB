using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;

namespace MorphDB.Npgsql.Pipeline.Transformers;

/// <summary>
/// Applies default values to fields that are missing or null.
/// Handles static, computed, and context-based defaults.
/// </summary>
public sealed class DefaultValueApplier : ITransformer
{
    public int Order => PipelineOrder.DefaultValueApplier;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ApplyDefaults
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        foreach (var column in context.Table.Columns.Where(c => c.IsActive))
        {
            // Skip if value already provided
            if (context.Data.TryGetValue(column.LogicalName, out var existingValue) && existingValue is not null)
            {
                continue;
            }

            // Apply default based on type
            var defaultValue = GetDefaultValue(column, context);
            if (defaultValue is not null)
            {
                context.Data[column.LogicalName] = defaultValue;
            }
        }

        return Task.CompletedTask;
    }

    private static object? GetDefaultValue(ColumnMetadata column, IWriteContext context)
    {
        return column.DefaultType switch
        {
            DefaultValueType.Static => ParseStaticDefault(column.DefaultValue, column.DataType),
            DefaultValueType.ContextBased => GetContextBasedDefault(column, context),
            DefaultValueType.Computed => GetComputedDefault(column, context),
            // DbFunction and AutoIncrement are handled by PostgreSQL
            _ => null
        };
    }

    private static object? ParseStaticDefault(string? defaultValue, MorphDataType dataType)
    {
        if (string.IsNullOrEmpty(defaultValue))
            return null;

        return dataType switch
        {
            MorphDataType.Text or MorphDataType.LongText or MorphDataType.Email or MorphDataType.Url or MorphDataType.Phone
                => defaultValue,
            MorphDataType.Integer => int.TryParse(defaultValue, out var i) ? i : null,
            MorphDataType.BigInteger => long.TryParse(defaultValue, out var l) ? l : null,
            MorphDataType.Decimal => decimal.TryParse(defaultValue, out var d) ? d : null,
            MorphDataType.Boolean => bool.TryParse(defaultValue, out var b) ? b : null,
            MorphDataType.Uuid => Guid.TryParse(defaultValue, out var g) ? g : null,
            MorphDataType.Date or MorphDataType.DateTime =>
                DateTimeOffset.TryParse(defaultValue, out var dt) ? dt : null,
            _ => defaultValue
        };
    }

    private static object? GetContextBasedDefault(ColumnMetadata column, IWriteContext context)
    {
        // Parse the default value as a context key
        var contextKey = column.DefaultValue?.Trim().ToLowerInvariant();

        return contextKey switch
        {
            "{{user_id}}" or "{{userid}}" => context.SecurityContext?.UserId,
            "{{project_id}}" or "{{projectid}}" => context.ProjectId,
            "{{user_email}}" or "{{email}}" => context.SecurityContext?.Email,
            "{{now}}" or "{{current_timestamp}}" => DateTimeOffset.UtcNow,
            "{{today}}" or "{{current_date}}" => DateTimeOffset.UtcNow.Date,
            "{{uuid}}" or "{{new_uuid}}" => Guid.NewGuid(),
            _ => null
        };
    }

    private static object? GetComputedDefault(ColumnMetadata column, IWriteContext context)
    {
        // Supported grammar: a bare field reference ("field1"), or exactly one binary arithmetic
        // operation between two operands ("field1 + field2", "field1 * 0.1"), each operand being
        // either a field reference or a numeric literal. Anything outside this grammar (nested
        // expressions, functions, multiple operators) is not attempted — same "can't compute, stay
        // silent" contract ParseStaticDefault already has for an unparsable static value.
        var expression = column.DefaultValue?.Trim();
        if (string.IsNullOrEmpty(expression))
            return null;

        if (context.Data.TryGetValue(expression, out var directValue))
        {
            return directValue;
        }

        var result = EvaluateArithmeticExpression(expression, context.Data);
        if (result is null)
            return null;

        return column.DataType switch
        {
            MorphDataType.Integer => (int)result.Value,
            MorphDataType.BigInteger => (long)result.Value,
            _ => result.Value
        };
    }

    private static readonly char[] ComputedOperators = ['+', '-', '*', '/'];

    private static decimal? EvaluateArithmeticExpression(string expression, IDictionary<string, object?> data)
    {
        foreach (var op in ComputedOperators)
        {
            var opIndex = expression.IndexOf(op);
            if (opIndex <= 0 || opIndex == expression.Length - 1)
                continue;

            var leftText = expression[..opIndex].Trim();
            var rightText = expression[(opIndex + 1)..].Trim();

            if (leftText.Length == 0 || rightText.Length == 0)
                continue;

            if (!TryResolveOperand(leftText, data, out var left) || !TryResolveOperand(rightText, data, out var right))
                continue;

            return op switch
            {
                '+' => left + right,
                '-' => left - right,
                '*' => left * right,
                '/' => right == 0m ? null : left / right,
                _ => null
            };
        }

        return null;
    }

    private static bool TryResolveOperand(string operand, IDictionary<string, object?> data, out decimal value)
    {
        if (decimal.TryParse(operand, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (data.TryGetValue(operand, out var fieldValue) && fieldValue is not null)
        {
            try
            {
                value = Convert.ToDecimal(fieldValue, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
        }

        value = default;
        return false;
    }
}
