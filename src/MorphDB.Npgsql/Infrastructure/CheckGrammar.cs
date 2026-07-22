using System.Text.RegularExpressions;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The single definition of the CHECK expression grammar (§3.10-C1: the app-layer parser is the
/// canon; CHECK is a virtual constraint, never emitted into DDL). A declaration is accepted only
/// when the evaluator can actually enforce it — accepting more grammar than the evaluator speaks
/// would let a declared constraint silently not constrain. Grammar: comparisons
/// (<c>field op value</c>, <c>field op field</c> with <c>&gt; &gt;= &lt; &lt;= = == != &lt;&gt;</c>),
/// <c>field MATCHES 'regex'</c>, combined with <c>AND</c>/<c>OR</c> and parentheses.
/// </summary>
public static partial class CheckGrammar
{
    public const string SupportedForms =
        "Supported CHECK forms: <field> <op> <value>, <field> <op> <field> " +
        "(op: > >= < <= = == != <>), <field> MATCHES '<regex>', " +
        "combined with AND / OR and parentheses.";

    /// <summary>
    /// Rejects an expression the evaluator cannot enforce. Declaration-time gate: a CHECK outside
    /// the grammar would be stored, never evaluated (the evaluator skips what it cannot parse),
    /// and the caller would trust a constraint that constrains nothing.
    /// </summary>
    public static void EnsureSupported(string expression)
    {
        if (!IsSupported(expression))
        {
            throw new ArgumentException(
                $"CHECK expression '{expression}' is not enforceable. {SupportedForms}");
        }
    }

    public static bool IsSupported(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        expression = expression.Trim();
        while (expression.StartsWith('(') && expression.EndsWith(')') && IsBalanced(expression[1..^1]))
        {
            expression = expression[1..^1].Trim();
        }

        var orParts = SplitByLogicalOperator(expression, "OR");
        if (orParts.Count > 1)
        {
            return orParts.All(IsSupported);
        }

        var andParts = SplitByLogicalOperator(expression, "AND");
        if (andParts.Count > 1)
        {
            return andParts.All(IsSupported);
        }

        return SimplePattern().IsMatch(expression)
            || CrossFieldPattern().IsMatch(expression)
            || MatchesPattern().IsMatch(expression);
    }

    /// <summary>Splits by a logical operator at depth zero, respecting parentheses.</summary>
    public static List<string> SplitByLogicalOperator(string expression, string op)
    {
        var result = new List<string>();
        var depth = 0;
        var lastSplit = 0;
        var opPattern = $" {op} ";
        var i = 0;

        while (i < expression.Length)
        {
            if (expression[i] == '(')
            {
                depth++;
            }
            else if (expression[i] == ')')
            {
                depth--;
            }
            else if (depth == 0
                && i + opPattern.Length <= expression.Length
                && expression.Substring(i, opPattern.Length).Equals(opPattern, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(expression[lastSplit..i].Trim());
                i += opPattern.Length;
                lastSplit = i;
                continue;
            }

            i++;
        }

        result.Add(expression[lastSplit..].Trim());
        return result;
    }

    public static bool IsBalanced(string expression)
    {
        var depth = 0;
        foreach (var c in expression)
        {
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }

            if (depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }

    [GeneratedRegex(@"^(?<field>\w+)\s*(?<op>>=|<=|==|!=|<>|[><=])\s*(?<value>'[^']*'|-?\d+(\.\d+)?|true|false|null)$", RegexOptions.IgnoreCase)]
    public static partial Regex SimplePattern();

    [GeneratedRegex(@"^(?<field1>\w+)\s*(?<op>>=|<=|==|!=|<>|[><=])\s*(?<field2>\w+)$")]
    public static partial Regex CrossFieldPattern();

    [GeneratedRegex(@"^(?<field>\w+)\s+MATCHES\s+'(?<pattern>[^']+)'$", RegexOptions.IgnoreCase)]
    public static partial Regex MatchesPattern();
}
