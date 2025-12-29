using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using System.Text.RegularExpressions;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates CHECK constraints at application layer.
/// Supports simple expressions like "price > 0" or "end_date > start_date".
/// </summary>
public sealed partial class CheckValidator : IValidator
{
    public int Order => PipelineOrder.CheckValidator;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ValidateCheck
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public Task ExecuteAsync(IWriteContext context)
    {
        var columnsWithCheck = context.Table.Columns
            .Where(c => !string.IsNullOrEmpty(c.CheckExpression) && c.IsActive)
            .ToList();

        foreach (var column in columnsWithCheck)
        {
            // Get the value for this column
            if (!context.Data.TryGetValue(column.LogicalName, out var value))
            {
                // For updates, use existing value if not in payload
                if (context.ExistingData?.TryGetValue(column.LogicalName, out value) != true)
                {
                    continue;
                }
            }

            // Evaluate the check expression
            var isValid = EvaluateCheckExpression(column.CheckExpression!, column.LogicalName, value, context.Data);

            if (!isValid)
            {
                ((WriteContext)context).AddError(
                    column.LogicalName,
                    ValidationErrorCodes.CheckViolation,
                    $"Value '{value}' violates check constraint: {column.CheckExpression}",
                    value);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Evaluates a simple check expression.
    /// Supports: field > value, field >= value, field < value, field <= value,
    ///           field1 > field2, field1 < field2, etc.
    /// </summary>
    private static bool EvaluateCheckExpression(
        string expression,
        string columnName,
        object? value,
        IDictionary<string, object?> data)
    {
        if (value is null) return true; // NULL values bypass check constraints

        expression = expression.Trim();

        // Pattern: field operator value
        // Examples: "price > 0", "quantity >= 1", "age < 150"
        var simpleMatch = SimpleCheckPattern().Match(expression);
        if (simpleMatch.Success)
        {
            var field = simpleMatch.Groups["field"].Value.Trim();
            var op = simpleMatch.Groups["op"].Value.Trim();
            var compareValue = simpleMatch.Groups["value"].Value.Trim();

            // If the field name matches the column, compare with the provided value
            if (field.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return CompareValues(value, op, ParseValue(compareValue));
            }
        }

        // Pattern: field1 operator field2
        // Examples: "end_date > start_date"
        var crossFieldMatch = CrossFieldCheckPattern().Match(expression);
        if (crossFieldMatch.Success)
        {
            var field1 = crossFieldMatch.Groups["field1"].Value.Trim();
            var op = crossFieldMatch.Groups["op"].Value.Trim();
            var field2 = crossFieldMatch.Groups["field2"].Value.Trim();

            object? value1 = null, value2 = null;

            if (field1.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                value1 = value;
                data.TryGetValue(field2, out value2);
            }
            else if (field2.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                value2 = value;
                data.TryGetValue(field1, out value1);
            }

            if (value1 is not null && value2 is not null)
            {
                return CompareValues(value1, op, value2);
            }
        }

        // Unknown expression format, assume valid
        return true;
    }

    private static bool CompareValues(object? left, string op, object? right)
    {
        if (left is null || right is null) return true;

        // Try numeric comparison
        if (TryConvertToDouble(left, out var leftNum) && TryConvertToDouble(right, out var rightNum))
        {
            return op switch
            {
                ">" => leftNum > rightNum,
                ">=" => leftNum >= rightNum,
                "<" => leftNum < rightNum,
                "<=" => leftNum <= rightNum,
                "=" or "==" => Math.Abs(leftNum - rightNum) < 0.0001,
                "!=" or "<>" => Math.Abs(leftNum - rightNum) >= 0.0001,
                _ => true
            };
        }

        // Try date comparison
        if (left is DateTimeOffset leftDate && right is DateTimeOffset rightDate)
        {
            return op switch
            {
                ">" => leftDate > rightDate,
                ">=" => leftDate >= rightDate,
                "<" => leftDate < rightDate,
                "<=" => leftDate <= rightDate,
                "=" or "==" => leftDate == rightDate,
                "!=" or "<>" => leftDate != rightDate,
                _ => true
            };
        }

        if (left is DateTime leftDt && right is DateTime rightDt)
        {
            return op switch
            {
                ">" => leftDt > rightDt,
                ">=" => leftDt >= rightDt,
                "<" => leftDt < rightDt,
                "<=" => leftDt <= rightDt,
                "=" or "==" => leftDt == rightDt,
                "!=" or "<>" => leftDt != rightDt,
                _ => true
            };
        }

        // String comparison
        if (left is string leftStr && right is string rightStr)
        {
            var comparison = string.Compare(leftStr, rightStr, StringComparison.Ordinal);
            return op switch
            {
                ">" => comparison > 0,
                ">=" => comparison >= 0,
                "<" => comparison < 0,
                "<=" => comparison <= 0,
                "=" or "==" => comparison == 0,
                "!=" or "<>" => comparison != 0,
                _ => true
            };
        }

        return true;
    }

    private static bool TryConvertToDouble(object? value, out double result)
    {
        result = 0;
        if (value is null) return false;

        return value switch
        {
            double d => (result = d) == d,
            float f => (result = f) == f,
            decimal m => (result = (double)m) == (double)m,
            int i => (result = i) == i,
            long l => (result = l) == l,
            short s => (result = s) == s,
            _ => double.TryParse(value.ToString(), out result)
        };
    }

    private static object? ParseValue(string value)
    {
        // Remove quotes if present
        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        // Try parse as number
        if (double.TryParse(value, out var num))
        {
            return num;
        }

        // Try parse as boolean
        if (bool.TryParse(value, out var boolVal))
        {
            return boolVal;
        }

        return value;
    }

    [GeneratedRegex(@"^(?<field>\w+)\s*(?<op>[><=!]+)\s*(?<value>.+)$")]
    private static partial Regex SimpleCheckPattern();

    [GeneratedRegex(@"^(?<field1>\w+)\s*(?<op>[><=!]+)\s*(?<field2>\w+)$")]
    private static partial Regex CrossFieldCheckPattern();
}
