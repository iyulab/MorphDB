using System.Text;
using System.Text.Json;
using MorphDB.Core.Abstractions;

namespace MorphDB.Core.Formula;

/// <summary>
/// Translates formula AST to PostgreSQL SQL expressions.
/// </summary>
public sealed class FormulaSqlTranslator
{
    private readonly IReadOnlyDictionary<string, string> _columnMappings;
    private readonly List<string> _errors = [];

    /// <summary>
    /// Creates a translator with column logical-to-physical mappings.
    /// </summary>
    /// <param name="columnMappings">Map of logical column names to physical column names.</param>
    public FormulaSqlTranslator(IReadOnlyDictionary<string, string> columnMappings)
    {
        _columnMappings = columnMappings;
    }

    /// <summary>
    /// Translates a formula AST (as JSON) to a PostgreSQL SQL expression.
    /// </summary>
    /// <param name="astJson">The AST in JSON format.</param>
    /// <param name="tableAlias">Optional table alias prefix for column references.</param>
    /// <returns>The SQL expression and any translation errors.</returns>
    public (string Sql, IReadOnlyList<string> Errors) Translate(string astJson, string? tableAlias = null)
    {
        _errors.Clear();

        try
        {
            var doc = JsonDocument.Parse(astJson);
            var sql = TranslateNode(doc.RootElement, tableAlias);
            return (sql, _errors);
        }
        catch (Exception ex)
        {
            return ("NULL", new[] { $"Translation error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Translates a FormulaNode directly to SQL.
    /// </summary>
    public string TranslateNode(FormulaNode node, string? tableAlias = null)
    {
        return node switch
        {
            LiteralNode literal => TranslateLiteral(literal),
            ColumnReferenceNode column => TranslateColumnReference(column.ColumnName, tableAlias),
            BinaryOperatorNode binary => TranslateBinaryOperator(binary, tableAlias),
            UnaryOperatorNode unary => TranslateUnaryOperator(unary, tableAlias),
            FunctionCallNode function => TranslateFunction(function, tableAlias),
            _ => throw new ArgumentException($"Unknown node type: {node.GetType().Name}")
        };
    }

    private string TranslateNode(JsonElement element, string? tableAlias)
    {
        var nodeType = element.GetProperty("nodeType").GetString();

        return nodeType switch
        {
            "Literal" => TranslateLiteral(element),
            "ColumnReference" => TranslateColumnReference(element, tableAlias),
            "BinaryOperator" => TranslateBinaryOperator(element, tableAlias),
            "UnaryOperator" => TranslateUnaryOperator(element, tableAlias),
            "FunctionCall" => TranslateFunction(element, tableAlias),
            _ => throw new ArgumentException($"Unknown node type: {nodeType}")
        };
    }

    private static string TranslateLiteral(LiteralNode node)
    {
        return node.Type switch
        {
            LiteralType.Null => "NULL",
            LiteralType.Boolean => node.Value is true ? "TRUE" : "FALSE",
            LiteralType.IntegerLiteral or LiteralType.DecimalLiteral =>
                node.Value is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : "NULL",
            LiteralType.StringLiteral => $"'{EscapeString(node.Value?.ToString() ?? "")}'",
            _ => "NULL"
        };
    }

    private static string TranslateLiteral(JsonElement element)
    {
        var type = element.GetProperty("type").GetInt32();

        return type switch
        {
            0 => "NULL", // LiteralType.Null
            1 => element.GetProperty("value").GetBoolean() ? "TRUE" : "FALSE", // Boolean
            2 => element.GetProperty("value").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture), // IntegerLiteral
            3 => element.GetProperty("value").GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture), // DecimalLiteral
            4 => $"'{EscapeString(element.GetProperty("value").GetString() ?? "")}'", // StringLiteral
            _ => "NULL"
        };
    }

    private string TranslateColumnReference(string columnName, string? tableAlias)
    {
        if (!_columnMappings.TryGetValue(columnName, out var physicalName))
        {
            _errors.Add($"Unknown column: {columnName}");
            return $"NULL /* unknown column: {columnName} */";
        }

        var quotedName = $"\"{physicalName}\"";
        return string.IsNullOrEmpty(tableAlias) ? quotedName : $"{tableAlias}.{quotedName}";
    }

    private string TranslateColumnReference(JsonElement element, string? tableAlias)
    {
        var columnName = element.GetProperty("columnName").GetString() ?? "";
        return TranslateColumnReference(columnName, tableAlias);
    }

    private string TranslateBinaryOperator(BinaryOperatorNode node, string? tableAlias)
    {
        var left = TranslateNode(node.Left, tableAlias);
        var right = TranslateNode(node.Right, tableAlias);
        var op = node.Operator;

        // Handle string concatenation
        if (op == "||")
        {
            return $"CONCAT({left}, {right})";
        }

        return $"({left} {op} {right})";
    }

    private string TranslateBinaryOperator(JsonElement element, string? tableAlias)
    {
        var op = element.GetProperty("operator").GetString();
        var left = TranslateNode(element.GetProperty("left"), tableAlias);
        var right = TranslateNode(element.GetProperty("right"), tableAlias);

        // Handle string concatenation
        if (op == "||")
        {
            return $"CONCAT({left}, {right})";
        }

        return $"({left} {op} {right})";
    }

    private string TranslateUnaryOperator(UnaryOperatorNode node, string? tableAlias)
    {
        var operand = TranslateNode(node.Operand, tableAlias);
        return node.Operator.ToUpperInvariant() switch
        {
            "NOT" => $"(NOT {operand})",
            "-" => $"(-{operand})",
            _ => operand
        };
    }

    private string TranslateUnaryOperator(JsonElement element, string? tableAlias)
    {
        var op = element.GetProperty("operator").GetString()?.ToUpperInvariant();
        var operand = TranslateNode(element.GetProperty("operand"), tableAlias);

        return op switch
        {
            "NOT" => $"(NOT {operand})",
            "-" => $"(-{operand})",
            _ => operand
        };
    }

    private string TranslateFunction(FunctionCallNode node, string? tableAlias)
    {
        var args = node.Arguments.Select(a => TranslateNode(a, tableAlias)).ToList();
        return TranslateFunctionCall(node.FunctionName, args);
    }

    private string TranslateFunction(JsonElement element, string? tableAlias)
    {
        var funcName = element.GetProperty("functionName").GetString() ?? "";
        var args = element.GetProperty("arguments").EnumerateArray()
            .Select(a => TranslateNode(a, tableAlias))
            .ToList();

        return TranslateFunctionCall(funcName, args);
    }

    private static string TranslateFunctionCall(string funcName, List<string> args)
    {
        var upperName = funcName.ToUpperInvariant();

        return upperName switch
        {
            // Conditional
            "IF" when args.Count >= 3 => $"CASE WHEN {args[0]} THEN {args[1]} ELSE {args[2]} END",
            "IF" when args.Count == 2 => $"CASE WHEN {args[0]} THEN {args[1]} ELSE NULL END",
            "IFS" => TranslateIfs(args),
            "SWITCH" => TranslateSwitch(args),
            "COALESCE" => $"COALESCE({string.Join(", ", args)})",
            "NULLIF" when args.Count >= 2 => $"NULLIF({args[0]}, {args[1]})",

            // String functions
            "CONCAT" => $"CONCAT({string.Join(", ", args)})",
            "UPPER" when args.Count >= 1 => $"UPPER({args[0]})",
            "LOWER" when args.Count >= 1 => $"LOWER({args[0]})",
            "TRIM" when args.Count >= 1 => $"TRIM({args[0]})",
            "LTRIM" when args.Count >= 1 => $"LTRIM({args[0]})",
            "RTRIM" when args.Count >= 1 => $"RTRIM({args[0]})",
            "LEFT" when args.Count >= 2 => $"LEFT({args[0]}, {args[1]})",
            "RIGHT" when args.Count >= 2 => $"RIGHT({args[0]}, {args[1]})",
            "SUBSTRING" when args.Count >= 3 => $"SUBSTRING({args[0]} FROM {args[1]} FOR {args[2]})",
            "SUBSTRING" when args.Count >= 2 => $"SUBSTRING({args[0]} FROM {args[1]})",
            "REPLACE" when args.Count >= 3 => $"REPLACE({args[0]}, {args[1]}, {args[2]})",
            "LENGTH" when args.Count >= 1 => $"LENGTH({args[0]})",
            "CHAR_LENGTH" when args.Count >= 1 => $"CHAR_LENGTH({args[0]})",

            // Numeric functions
            "ABS" when args.Count >= 1 => $"ABS({args[0]})",
            "ROUND" when args.Count >= 2 => $"ROUND({args[0]}, {args[1]})",
            "ROUND" when args.Count >= 1 => $"ROUND({args[0]})",
            "FLOOR" when args.Count >= 1 => $"FLOOR({args[0]})",
            "CEIL" or "CEILING" when args.Count >= 1 => $"CEIL({args[0]})",
            "MOD" when args.Count >= 2 => $"MOD({args[0]}, {args[1]})",
            "POWER" when args.Count >= 2 => $"POWER({args[0]}, {args[1]})",
            "SQRT" when args.Count >= 1 => $"SQRT({args[0]})",
            "LOG" when args.Count >= 1 => $"LN({args[0]})",
            "LOG10" when args.Count >= 1 => $"LOG({args[0]})",
            "EXP" when args.Count >= 1 => $"EXP({args[0]})",
            "SIGN" when args.Count >= 1 => $"SIGN({args[0]})",

            // Date functions
            "NOW" or "CURRENT_TIMESTAMP" => "NOW()",
            "TODAY" or "CURRENT_DATE" => "CURRENT_DATE",
            "CURRENT_TIME" => "CURRENT_TIME",
            "DATE" when args.Count >= 1 => $"({args[0]})::date",
            "YEAR" when args.Count >= 1 => $"EXTRACT(YEAR FROM {args[0]})",
            "MONTH" when args.Count >= 1 => $"EXTRACT(MONTH FROM {args[0]})",
            "DAY" when args.Count >= 1 => $"EXTRACT(DAY FROM {args[0]})",
            "HOUR" when args.Count >= 1 => $"EXTRACT(HOUR FROM {args[0]})",
            "MINUTE" when args.Count >= 1 => $"EXTRACT(MINUTE FROM {args[0]})",
            "SECOND" when args.Count >= 1 => $"EXTRACT(SECOND FROM {args[0]})",
            "DATEADD" when args.Count >= 3 => TranslateDateAdd(args),
            "DATEDIFF" when args.Count >= 2 => $"({args[1]}::date - {args[0]}::date)",
            "DATEDIFF" when args.Count >= 3 => TranslateDateDiff(args),
            "DATE_TRUNC" when args.Count >= 2 => $"DATE_TRUNC({args[0]}, {args[1]})",

            // Boolean functions (already handled as operators mostly)
            "AND" => $"({string.Join(" AND ", args)})",
            "OR" => $"({string.Join(" OR ", args)})",
            "NOT" when args.Count >= 1 => $"(NOT {args[0]})",

            // Type conversion
            "CAST" when args.Count >= 2 => $"CAST({args[0]} AS {args[1]})",
            "TO_TEXT" when args.Count >= 1 => $"({args[0]})::text",
            "TO_NUMBER" when args.Count >= 1 => $"({args[0]})::numeric",
            "TO_DATE" when args.Count >= 1 => $"({args[0]})::date",
            "TO_BOOLEAN" when args.Count >= 1 => $"({args[0]})::boolean",

            // Misc
            "BLANK" => "NULL",
            "VALUE" when args.Count >= 1 => $"({args[0]})::numeric",
            "TEXT" when args.Count >= 1 => $"({args[0]})::text",

            // Aggregations (should only be used with rollups, but translate anyway)
            "SUM" when args.Count >= 1 => $"SUM({args[0]})",
            "AVG" when args.Count >= 1 => $"AVG({args[0]})",
            "MIN" when args.Count >= 1 => $"MIN({args[0]})",
            "MAX" when args.Count >= 1 => $"MAX({args[0]})",
            "COUNT" when args.Count >= 1 => $"COUNT({args[0]})",
            "COUNT" => "COUNT(*)",

            // Default: pass through as-is
            _ => $"{upperName}({string.Join(", ", args)})"
        };
    }

    private static string TranslateIfs(List<string> args)
    {
        // IFS(condition1, value1, condition2, value2, ...)
        if (args.Count < 2)
        {
            return "NULL";
        }

        var sb = new StringBuilder("CASE");
        for (var i = 0; i < args.Count - 1; i += 2)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" WHEN {args[i]} THEN {args[i + 1]}");
        }

        // If odd number of args, last one is the ELSE
        if (args.Count % 2 == 1)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" ELSE {args[^1]}");
        }

        sb.Append(" END");
        return sb.ToString();
    }

    private static string TranslateSwitch(List<string> args)
    {
        // SWITCH(expr, pattern1, value1, pattern2, value2, ..., [default])
        if (args.Count < 3)
        {
            return "NULL";
        }

        var expr = args[0];
        var sb = new StringBuilder();
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"CASE {expr}");

        for (var i = 1; i < args.Count - 1; i += 2)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" WHEN {args[i]} THEN {args[i + 1]}");
        }

        // If even number of args after expr, last one is the default
        if ((args.Count - 1) % 2 == 1)
        {
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $" ELSE {args[^1]}");
        }

        sb.Append(" END");
        return sb.ToString();
    }

    private static string TranslateDateAdd(List<string> args)
    {
        // DATEADD(interval, count, date)
        // PostgreSQL: date + interval '1 day'
        var interval = args[0].Trim('\'');
        var count = args[1];
        var date = args[2];

        return $"({date} + ({count} * INTERVAL '1 {interval}'))";
    }

    private static string TranslateDateDiff(List<string> args)
    {
        // DATEDIFF(interval, date1, date2)
        var interval = args[0].Trim('\'').ToUpperInvariant();
        var date1 = args[1];
        var date2 = args[2];

        return interval switch
        {
            "DAY" or "DAYS" => $"(({date2})::date - ({date1})::date)",
            "MONTH" or "MONTHS" => $"(EXTRACT(YEAR FROM {date2}) * 12 + EXTRACT(MONTH FROM {date2}) - EXTRACT(YEAR FROM {date1}) * 12 - EXTRACT(MONTH FROM {date1}))",
            "YEAR" or "YEARS" => $"(EXTRACT(YEAR FROM {date2}) - EXTRACT(YEAR FROM {date1}))",
            "HOUR" or "HOURS" => $"(EXTRACT(EPOCH FROM ({date2} - {date1})) / 3600)",
            "MINUTE" or "MINUTES" => $"(EXTRACT(EPOCH FROM ({date2} - {date1})) / 60)",
            "SECOND" or "SECONDS" => $"EXTRACT(EPOCH FROM ({date2} - {date1}))",
            _ => $"(({date2})::date - ({date1})::date)"
        };
    }

    private static string EscapeString(string value)
    {
        return value.Replace("'", "''");
    }
}
