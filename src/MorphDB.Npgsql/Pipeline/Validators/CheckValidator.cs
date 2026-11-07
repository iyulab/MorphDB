using System.Text.RegularExpressions;
using Dapper;
using MorphDB.Core.Models;
using MorphDB.Core.Pipeline;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Npgsql.Pipeline.Validators;

/// <summary>
/// Validates CHECK constraints at application layer.
/// Supports simple expressions like "price > 0" or "end_date > start_date".
/// Also supports compound expressions with AND/OR operators.
/// Examples: "price > 0 AND quantity >= 1", "status = 'active' OR status = 'pending'"
/// </summary>
public sealed partial class CheckValidator : IValidator
{
    private readonly MorphConnectionFactory? _connections;

    /// <param name="connections">
    /// Where a stored row is read from when a change names only some of the columns a CHECK compares. Without it
    /// (a unit test of the evaluator) the CHECK is judged by the request alone.
    /// </param>
    public CheckValidator(MorphConnectionFactory? connections = null)
    {
        _connections = connections;
    }

    public int Order => PipelineOrder.CheckValidator;

    public bool ReadsStoredRows(IWriteContext context) =>
        context.ExistingData is null && _connections is not null
        && context.OperationType is WriteOperationType.Update or WriteOperationType.Upsert
        && StoredColumnsNeeded(context.Table, context.Data).Count > 0;

    public bool ShouldExecute(IWriteContext context)
    {
        return context.Options.ValidateCheck
            && context.OperationType is WriteOperationType.Insert or WriteOperationType.Update or WriteOperationType.Upsert;
    }

    public async Task ExecuteAsync(IWriteContext context)
    {
        var isChange = context.OperationType is WriteOperationType.Update or WriteOperationType.Upsert;

        // A change is judged on the row it leaves behind: what the request names over what the row stores. A CHECK
        // that compares the changed column with one the request does not carry (hi >= lo, and only hi changes) is
        // otherwise a comparison with nothing, and passes. A CHECK none of whose columns the request names is
        // unchanged by it, and is not asked again — nor is the stored row read for it.
        var checks = isChange ? ChecksTouchedBy(context.Table, context.Data) : CheckedColumns(context.Table).ToList();
        if (checks.Count == 0)
            return;

        var stored = isChange ? context.ExistingData ?? await ReadStoredRowAsync(context) : null;

        foreach (var violation in Violations(checks, context.Data, stored))
        {
            ((WriteContext)context).AddError(violation.Field, ValidationErrorCodes.CheckViolation, violation.Message, violation.Value);
        }
    }

    /// <summary>A CHECK a change would leave broken: the column that carries it, what it says, and the value it stands on.</summary>
    internal sealed record CheckViolation(string Field, string Message, object? Value);

    /// <summary>
    /// The CHECKs that <paramref name="change"/> leaves broken, judged on the row it makes: the stored values
    /// (<paramref name="stored"/>, when there is one) under the ones the change names.
    /// </summary>
    internal static IEnumerable<CheckViolation> Violations(
        IEnumerable<ColumnMetadata> checks,
        IDictionary<string, object?> change,
        IDictionary<string, object?>? stored)
    {
        IDictionary<string, object?> row = change;
        if (stored is not null)
        {
            row = new Dictionary<string, object?>(stored, StringComparer.OrdinalIgnoreCase);
            foreach (var (field, named) in change)
                row[field] = named;
        }

        foreach (var column in checks)
        {
            if (!row.TryGetValue(column.LogicalName, out var value))
                continue;

            // REST binding delivers values as JsonElement; the in-memory comparisons below
            // (`is string`, MATCHES) do not match a JsonElement and would silently bypass the
            // constraint. Unwrap to a CLR value so string/regex CHECKs are actually enforced.
            value = JsonValueConverter.ToClrValue(value);

            if (!EvaluateCheckExpression(column.CheckExpression!, column.LogicalName, value, row))
            {
                yield return new CheckViolation(
                    column.LogicalName,
                    change.ContainsKey(column.LogicalName)
                        ? $"Value '{value}' violates check constraint: {column.CheckExpression}"
                        : $"The row would violate check constraint '{column.CheckExpression}': '{column.LogicalName}' is {value} and this change leaves it so.",
                    value);
            }
        }
    }

    private static IEnumerable<ColumnMetadata> CheckedColumns(TableMetadata table) =>
        table.Columns.Where(c => !string.IsNullOrEmpty(c.CheckExpression) && c.IsActive);

    /// <summary>The columns a CHECK reads: its own and every one its expression names.</summary>
    private static IEnumerable<string> ReferencedFields(ColumnMetadata column) =>
        CheckGrammar.Fields(column.CheckExpression!).Append(column.LogicalName);

    /// <summary>The CHECKs of <paramref name="table"/> that read at least one column <paramref name="change"/> names.</summary>
    internal static List<ColumnMetadata> ChecksTouchedBy(TableMetadata table, IDictionary<string, object?> change) =>
        [.. CheckedColumns(table).Where(c => ReferencedFields(c).Any(change.ContainsKey))];

    /// <summary>
    /// The columns, not named by <paramref name="change"/>, that the CHECKs <paramref name="change"/> touches read:
    /// what has to be taken from the stored row to judge them. Empty when no CHECK is split between the change and the row.
    /// </summary>
    internal static IReadOnlyList<ColumnMetadata> StoredColumnsNeeded(TableMetadata table, IDictionary<string, object?> change)
    {
        var logical = ChecksTouchedBy(table, change)
            .SelectMany(ReferencedFields)
            .Where(f => !change.ContainsKey(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return [.. table.Columns.Where(c => logical.Contains(c.LogicalName, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// The row the change lands on, as the pipeline's own data shape, or null when it cannot be read (no connection,
    /// no id or key, no such row — an insert-shaped upsert, or a row the caller's policy does not show), in which case
    /// the CHECK is judged by the request alone, as an insert's is.
    /// </summary>
    private async Task<IDictionary<string, object?>?> ReadStoredRowAsync(IWriteContext context)
    {
        if (_connections is null || StoredColumnsNeeded(context.Table, context.Data).Count == 0)
            return null;

        var table = context.Table;
        var parameters = new DynamicParameters();
        string where;
        if (context.OperationType == WriteOperationType.Update)
        {
            var pk = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is null || !context.RecordId.HasValue)
                return null;

            parameters.Add("id", context.RecordId.Value);
            where = $"{pk.PhysicalName} = @id" + (table.SoftDeleteEnabled ? " AND _deleted_at IS NULL" : string.Empty);
        }
        else
        {
            var keyNames = context.KeyColumns is { Count: > 0 } named
                ? named
                : [table.Columns.First(c => c.IsPrimaryKey).LogicalName];
            var conditions = new List<string>();
            for (var i = 0; i < keyNames.Count; i++)
            {
                var key = table.Columns.FirstOrDefault(c => c.LogicalName.Equals(keyNames[i], StringComparison.OrdinalIgnoreCase));
                if (key is null || !context.Data.TryGetValue(key.LogicalName, out var keyValue) || keyValue is null)
                    return null;

                parameters.Add($"k{i}", TypeMapper.ToDbValue(keyValue, key.DataType));
                conditions.Add($"{key.PhysicalName} = @k{i}");
            }

            where = string.Join(" AND ", conditions);
        }

        await using var conn = await _connections.LeaseAsync(context.CancellationToken);
        var row = await conn.Connection.QuerySingleOrDefaultAsync<dynamic>(
            new CommandDefinition($"SELECT * FROM {table.PhysicalName} WHERE {where}", parameters, transaction: conn.Transaction, cancellationToken: context.CancellationToken));

        return row is null ? null : RowMapper.MapToLogicalDictionary(row, table.Columns);
    }

    /// <summary>
    /// Evaluates a check expression that may contain AND/OR compound expressions.
    /// Supports: field &gt; value, field &gt;= value, field &lt; value, field &lt;= value,
    ///           field1 &gt; field2, field1 &lt; field2, etc.
    /// Also supports: expr1 AND expr2, expr1 OR expr2, and nested expressions.
    /// </summary>
    private static bool EvaluateCheckExpression(
        string expression,
        string columnName,
        object? value,
        IDictionary<string, object?> data)
    {
        if (value is null)
            return true; // NULL values bypass check constraints

        expression = expression.Trim();

        // Remove outer parentheses if present
        while (expression.StartsWith('(') && expression.EndsWith(')') && Infrastructure.CheckGrammar.IsBalanced(expression[1..^1]))
        {
            expression = expression[1..^1].Trim();
        }

        // Check for OR operator (lowest precedence, evaluated first to split)
        var orSplit = Infrastructure.CheckGrammar.SplitByLogicalOperator(expression, "OR");
        if (orSplit.Count > 1)
        {
            // OR: true if any sub-expression is true
            return orSplit.Any(subExpr => EvaluateCheckExpression(subExpr, columnName, value, data));
        }

        // Check for AND operator
        var andSplit = Infrastructure.CheckGrammar.SplitByLogicalOperator(expression, "AND");
        if (andSplit.Count > 1)
        {
            // AND: true only if all sub-expressions are true
            return andSplit.All(subExpr => EvaluateCheckExpression(subExpr, columnName, value, data));
        }

        // Evaluate as simple expression
        return EvaluateSimpleExpression(expression, columnName, value, data);
    }

    /// <summary>
    /// Evaluates a simple (non-compound) check expression.
    /// </summary>
    private static bool EvaluateSimpleExpression(
        string expression,
        string columnName,
        object? value,
        IDictionary<string, object?> data)
    {
        expression = expression.Trim();

        // Remove parentheses if present
        while (expression.StartsWith('(') && expression.EndsWith(')'))
        {
            expression = expression[1..^1].Trim();
        }

        // Pattern: field operator value
        // Examples: "price > 0", "quantity >= 1", "age < 150"
        var simpleMatch = Infrastructure.CheckGrammar.SimplePattern().Match(expression);
        if (simpleMatch.Success)
        {
            var field = simpleMatch.Groups["field"].Value.Trim();
            var op = simpleMatch.Groups["op"].Value.Trim();
            var compareValue = simpleMatch.Groups["value"].Value.Trim();

            // Get the value for this field
            var fieldValue = GetFieldValue(field, columnName, value, data);
            if (fieldValue is not null)
            {
                return CompareValues(fieldValue, op, ParseValue(compareValue));
            }
        }

        // Pattern: field1 operator field2
        // Examples: "end_date > start_date"
        var crossFieldMatch = Infrastructure.CheckGrammar.CrossFieldPattern().Match(expression);
        if (crossFieldMatch.Success)
        {
            var field1 = crossFieldMatch.Groups["field1"].Value.Trim();
            var op = crossFieldMatch.Groups["op"].Value.Trim();
            var field2 = crossFieldMatch.Groups["field2"].Value.Trim();

            var value1 = GetFieldValue(field1, columnName, value, data);
            var value2 = GetFieldValue(field2, columnName, value, data);

            if (value1 is not null && value2 is not null)
            {
                return CompareValues(value1, op, value2);
            }
        }

        // Pattern: field MATCHES 'regex_pattern'
        // Supports regex validation for CHECK expressions generated from @pattern annotations.
        var matchesMatch = Infrastructure.CheckGrammar.MatchesPattern().Match(expression);
        if (matchesMatch.Success)
        {
            var field = matchesMatch.Groups["field"].Value.Trim();
            var pattern = matchesMatch.Groups["pattern"].Value;

            var fieldValue = GetFieldValue(field, columnName, value, data);
            if (fieldValue is string strValue)
            {
                try
                {
                    return Regex.IsMatch(strValue, pattern);
                }
                catch
                {
                    return true; // Invalid regex patterns pass validation
                }
            }
            return true; // Non-string or null values pass
        }

        // Unknown expression format, assume valid
        return true;
    }

    /// <summary>
    /// Gets the value for a field, either from the current column or from the data dictionary.
    /// </summary>
    private static object? GetFieldValue(string field, string columnName, object? currentValue, IDictionary<string, object?> data)
    {
        if (field.Equals(columnName, StringComparison.OrdinalIgnoreCase))
        {
            return currentValue;
        }

        // Cross-field lookups read straight from the payload, so unwrap JsonElement here too.
        return data.TryGetValue(field, out var fieldValue) ? JsonValueConverter.ToClrValue(fieldValue) : null;
    }

    private static bool CompareValues(object? left, string op, object? right)
    {
        if (left is null || right is null)
            return true;

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

        // MATCHES / ~ operator (regex)
        if (op is "MATCHES" or "~")
        {
            if (left is string s && right is string p)
            {
                try
                {
                    return Regex.IsMatch(s, p);
                }
                catch
                {
                    return true;
                }
            }
            return true;
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
        if (value is null)
            return false;

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
}
