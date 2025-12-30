using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MorphDB.Core.Abstractions;

namespace MorphDB.Core.Formula;

/// <summary>
/// Parses formula expressions into an AST for evaluation and SQL translation.
/// Supports Airtable-like formula syntax with column references, operators, and functions.
/// </summary>
public sealed partial class FormulaParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> VolatileFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "NOW", "TODAY", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME", "RANDOM"
    };

    private static readonly HashSet<string> SupportedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // String functions
        "CONCAT", "UPPER", "LOWER", "TRIM", "LTRIM", "RTRIM", "LEFT", "RIGHT",
        "SUBSTRING", "REPLACE", "LENGTH", "CHAR_LENGTH",

        // Numeric functions
        "ABS", "ROUND", "FLOOR", "CEIL", "CEILING", "MOD", "POWER", "SQRT",
        "LOG", "LOG10", "EXP", "SIGN",

        // Date functions
        "NOW", "TODAY", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND",
        "DATEADD", "DATEDIFF", "DATE_TRUNC", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",

        // Conditional functions
        "IF", "IFS", "SWITCH", "COALESCE", "NULLIF",

        // Boolean functions
        "AND", "OR", "NOT",

        // Aggregation (for use with lookups)
        "SUM", "AVG", "MIN", "MAX", "COUNT",

        // Type conversion
        "CAST", "TO_TEXT", "TO_NUMBER", "TO_DATE", "TO_BOOLEAN",

        // Misc
        "BLANK", "VALUE", "TEXT", "RECORD_ID", "CREATED_TIME", "LAST_MODIFIED_TIME"
    };

    private string _formula = "";
    private int _position;
    private readonly List<string> _errors = [];
    private readonly List<string> _columnReferences = [];
    private readonly List<string> _functionCalls = [];
    private bool _isVolatile;

    /// <summary>
    /// Parses a formula expression and returns the parse result.
    /// </summary>
    public FormulaParseResult Parse(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return FormulaParseResult.Failure("Formula cannot be empty");
        }

        _formula = formula.Trim();
        _position = 0;
        _errors.Clear();
        _columnReferences.Clear();
        _functionCalls.Clear();
        _isVolatile = false;

        try
        {
            var ast = ParseExpression();

            if (_position < _formula.Length)
            {
                _errors.Add($"Unexpected character at position {_position}: '{_formula[_position]}'");
            }

            if (_errors.Count > 0)
            {
                return FormulaParseResult.Failure(_errors.ToArray());
            }

            var astJson = JsonSerializer.Serialize(ast, JsonOptions);

            return FormulaParseResult.Success(
                astJson,
                _columnReferences.Distinct().ToList(),
                _functionCalls.Distinct().ToList(),
                _isVolatile);
        }
        catch (Exception ex)
        {
            return FormulaParseResult.Failure($"Parse error: {ex.Message}");
        }
    }

    private FormulaNode ParseExpression()
    {
        return ParseOrExpression();
    }

    private FormulaNode ParseOrExpression()
    {
        var left = ParseAndExpression();

        while (MatchKeyword("OR"))
        {
            var right = ParseAndExpression();
            left = new BinaryOperatorNode { Operator = "OR", Left = left, Right = right };
        }

        return left;
    }

    private FormulaNode ParseAndExpression()
    {
        var left = ParseComparisonExpression();

        while (MatchKeyword("AND"))
        {
            var right = ParseComparisonExpression();
            left = new BinaryOperatorNode { Operator = "AND", Left = left, Right = right };
        }

        return left;
    }

    private FormulaNode ParseComparisonExpression()
    {
        var left = ParseAddSubtractExpression();

        if (Match("<="))
        {
            return new BinaryOperatorNode { Operator = "<=", Left = left, Right = ParseAddSubtractExpression() };
        }
        if (Match(">="))
        {
            return new BinaryOperatorNode { Operator = ">=", Left = left, Right = ParseAddSubtractExpression() };
        }
        if (Match("<>") || Match("!="))
        {
            return new BinaryOperatorNode { Operator = "<>", Left = left, Right = ParseAddSubtractExpression() };
        }
        if (Match("<"))
        {
            return new BinaryOperatorNode { Operator = "<", Left = left, Right = ParseAddSubtractExpression() };
        }
        if (Match(">"))
        {
            return new BinaryOperatorNode { Operator = ">", Left = left, Right = ParseAddSubtractExpression() };
        }
        if (Match("="))
        {
            return new BinaryOperatorNode { Operator = "=", Left = left, Right = ParseAddSubtractExpression() };
        }

        return left;
    }

    private FormulaNode ParseAddSubtractExpression()
    {
        var left = ParseMultiplyDivideExpression();

        while (true)
        {
            if (Match("+"))
            {
                left = new BinaryOperatorNode { Operator = "+", Left = left, Right = ParseMultiplyDivideExpression() };
            }
            else if (Match("-"))
            {
                left = new BinaryOperatorNode { Operator = "-", Left = left, Right = ParseMultiplyDivideExpression() };
            }
            else if (Match("&"))
            {
                // String concatenation
                left = new BinaryOperatorNode { Operator = "||", Left = left, Right = ParseMultiplyDivideExpression() };
            }
            else
            {
                break;
            }
        }

        return left;
    }

    private FormulaNode ParseMultiplyDivideExpression()
    {
        var left = ParseUnaryExpression();

        while (true)
        {
            if (Match("*"))
            {
                left = new BinaryOperatorNode { Operator = "*", Left = left, Right = ParseUnaryExpression() };
            }
            else if (Match("/"))
            {
                left = new BinaryOperatorNode { Operator = "/", Left = left, Right = ParseUnaryExpression() };
            }
            else if (Match("%"))
            {
                left = new BinaryOperatorNode { Operator = "%", Left = left, Right = ParseUnaryExpression() };
            }
            else
            {
                break;
            }
        }

        return left;
    }

    private FormulaNode ParseUnaryExpression()
    {
        if (MatchKeyword("NOT"))
        {
            return new UnaryOperatorNode { Operator = "NOT", Operand = ParseUnaryExpression() };
        }
        if (Match("-"))
        {
            return new UnaryOperatorNode { Operator = "-", Operand = ParseUnaryExpression() };
        }

        return ParsePrimaryExpression();
    }

    private FormulaNode ParsePrimaryExpression()
    {
        SkipWhitespace();

        // Parenthesized expression
        if (Match("("))
        {
            var expr = ParseExpression();
            Expect(")");
            return expr;
        }

        // Column reference: {column_name}
        if (Match("{"))
        {
            var columnName = ParseUntil('}');
            Expect("}");
            _columnReferences.Add(columnName);
            return new ColumnReferenceNode { ColumnName = columnName };
        }

        // String literal: "text" or 'text'
        if (Peek() == '"' || Peek() == '\'')
        {
            var quote = Advance();
            var value = ParseUntil(quote);
            Expect(quote.ToString());
            return new LiteralNode { Value = value, Type = LiteralType.StringLiteral };
        }

        // Number literal
        if (char.IsDigit(Peek()) || (Peek() == '.' && _position + 1 < _formula.Length && char.IsDigit(_formula[_position + 1])))
        {
            return ParseNumber();
        }

        // Boolean literal or function/keyword
        var identifier = ParseIdentifier();
        if (string.IsNullOrEmpty(identifier))
        {
            _errors.Add($"Expected expression at position {_position}");
            return new LiteralNode { Value = null, Type = LiteralType.Null };
        }

        // Boolean literals
        if (identifier.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralNode { Value = true, Type = LiteralType.Boolean };
        }
        if (identifier.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralNode { Value = false, Type = LiteralType.Boolean };
        }
        if (identifier.Equals("NULL", StringComparison.OrdinalIgnoreCase) ||
            identifier.Equals("BLANK", StringComparison.OrdinalIgnoreCase))
        {
            return new LiteralNode { Value = null, Type = LiteralType.Null };
        }

        // Function call
        SkipWhitespace();
        if (Peek() == '(')
        {
            Advance(); // consume '('
            var args = ParseFunctionArguments();
            Expect(")");

            _functionCalls.Add(identifier);
            if (VolatileFunctions.Contains(identifier))
            {
                _isVolatile = true;
            }

            if (!SupportedFunctions.Contains(identifier))
            {
                _errors.Add($"Unknown function: {identifier}");
            }

            return new FunctionCallNode { FunctionName = identifier.ToUpperInvariant(), Arguments = args };
        }

        // Bare identifier (treat as column reference)
        _columnReferences.Add(identifier);
        return new ColumnReferenceNode { ColumnName = identifier };
    }

    private List<FormulaNode> ParseFunctionArguments()
    {
        var args = new List<FormulaNode>();

        SkipWhitespace();
        if (Peek() == ')')
        {
            return args;
        }

        args.Add(ParseExpression());

        while (Match(","))
        {
            args.Add(ParseExpression());
        }

        return args;
    }

    private LiteralNode ParseNumber()
    {
        var sb = new StringBuilder();
        var hasDecimal = false;

        while (_position < _formula.Length)
        {
            var c = _formula[_position];
            if (char.IsDigit(c))
            {
                sb.Append(c);
                _position++;
            }
            else if (c == '.' && !hasDecimal)
            {
                sb.Append(c);
                hasDecimal = true;
                _position++;
            }
            else
            {
                break;
            }
        }

        var numStr = sb.ToString();
        if (hasDecimal)
        {
            if (decimal.TryParse(numStr, out var decVal))
            {
                return new LiteralNode { Value = decVal, Type = LiteralType.DecimalLiteral };
            }
        }
        else
        {
            if (long.TryParse(numStr, out var longVal))
            {
                return new LiteralNode { Value = longVal, Type = LiteralType.IntegerLiteral };
            }
        }

        _errors.Add($"Invalid number: {numStr}");
        return new LiteralNode { Value = 0, Type = LiteralType.IntegerLiteral };
    }

    private string ParseIdentifier()
    {
        SkipWhitespace();
        var sb = new StringBuilder();

        if (_position < _formula.Length && (char.IsLetter(_formula[_position]) || _formula[_position] == '_'))
        {
            sb.Append(_formula[_position]);
            _position++;

            while (_position < _formula.Length &&
                   (char.IsLetterOrDigit(_formula[_position]) || _formula[_position] == '_'))
            {
                sb.Append(_formula[_position]);
                _position++;
            }
        }

        return sb.ToString();
    }

    private string ParseUntil(char terminator)
    {
        var sb = new StringBuilder();
        while (_position < _formula.Length && _formula[_position] != terminator)
        {
            if (_formula[_position] == '\\' && _position + 1 < _formula.Length)
            {
                // Handle escape sequences
                _position++;
                sb.Append(_formula[_position]);
            }
            else
            {
                sb.Append(_formula[_position]);
            }
            _position++;
        }
        return sb.ToString();
    }

    private void SkipWhitespace()
    {
        while (_position < _formula.Length && char.IsWhiteSpace(_formula[_position]))
        {
            _position++;
        }
    }

    private char Peek()
    {
        SkipWhitespace();
        return _position < _formula.Length ? _formula[_position] : '\0';
    }

    private char Advance()
    {
        SkipWhitespace();
        return _position < _formula.Length ? _formula[_position++] : '\0';
    }

    private bool Match(string expected)
    {
        SkipWhitespace();
        if (_position + expected.Length <= _formula.Length &&
            _formula.Substring(_position, expected.Length) == expected)
        {
            _position += expected.Length;
            return true;
        }
        return false;
    }

    private bool MatchKeyword(string keyword)
    {
        SkipWhitespace();
        if (_position + keyword.Length <= _formula.Length &&
            _formula.Substring(_position, keyword.Length).Equals(keyword, StringComparison.OrdinalIgnoreCase))
        {
            // Ensure it's a complete word (not part of identifier)
            var endPos = _position + keyword.Length;
            if (endPos >= _formula.Length || !char.IsLetterOrDigit(_formula[endPos]))
            {
                _position = endPos;
                return true;
            }
        }
        return false;
    }

    private void Expect(string expected)
    {
        if (!Match(expected))
        {
            _errors.Add($"Expected '{expected}' at position {_position}");
        }
    }
}

/// <summary>
/// Base class for formula AST nodes.
/// </summary>
public abstract class FormulaNode
{
    public abstract string NodeType { get; }
}

/// <summary>
/// Literal value node (number, string, boolean, null).
/// </summary>
public sealed class LiteralNode : FormulaNode
{
    public override string NodeType => "Literal";
    public object? Value { get; init; }
    public LiteralType Type { get; init; }
}

/// <summary>
/// Column reference node.
/// </summary>
public sealed class ColumnReferenceNode : FormulaNode
{
    public override string NodeType => "ColumnReference";
    public required string ColumnName { get; init; }
}

/// <summary>
/// Binary operator node (+, -, *, /, =, etc.).
/// </summary>
public sealed class BinaryOperatorNode : FormulaNode
{
    public override string NodeType => "BinaryOperator";
    public required string Operator { get; init; }
    public required FormulaNode Left { get; init; }
    public required FormulaNode Right { get; init; }
}

/// <summary>
/// Unary operator node (NOT, -).
/// </summary>
public sealed class UnaryOperatorNode : FormulaNode
{
    public override string NodeType => "UnaryOperator";
    public required string Operator { get; init; }
    public required FormulaNode Operand { get; init; }
}

/// <summary>
/// Function call node.
/// </summary>
public sealed class FunctionCallNode : FormulaNode
{
    public override string NodeType => "FunctionCall";
    public required string FunctionName { get; init; }
    public required List<FormulaNode> Arguments { get; init; }
}

/// <summary>
/// Literal value types.
/// </summary>
public enum LiteralType
{
    Null,
    Boolean,
    IntegerLiteral,
    DecimalLiteral,
    StringLiteral
}
