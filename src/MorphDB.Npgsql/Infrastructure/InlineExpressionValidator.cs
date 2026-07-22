using MorphDB.Core.Exceptions;

namespace MorphDB.Npgsql.Infrastructure;

/// <summary>
/// The single gate for a caller-authored predicate that reaches SQL verbatim.
/// <para>
/// Check constraints, index predicates and row-level security expressions are all free-form, so
/// none can be matched against a fixed set the way a function default can. Each is emitted inside
/// parentheses, though, and escaping that form requires a closing parenthesis with no opener of its
/// own — which is exactly what unbalanced counting detects. Quoted text is skipped so that
/// parentheses inside a string literal or a quoted identifier do not count. Statement separators
/// and comment openers are refused outright: neither belongs in a predicate, both could truncate
/// the statement, and a predicate frequently sits at the end of its statement where a separator is
/// all an escape would need.
/// </para>
/// <para>
/// Anything this cannot account for is refused rather than passed through. This lives outside
/// <c>DdlBuilder</c> because it stopped being about DDL: a security policy's expression is spliced
/// into the WHERE clause of ordinary queries, and that path shipped with no validation at all while
/// the DDL paths beside it were guarded — one rule with two homes is how the second home ends up
/// forgotten.
/// </para>
/// </summary>
internal static class InlineExpressionValidator
{
    /// <summary>
    /// Refuses <paramref name="expression"/> if it could escape the predicate it will be emitted
    /// into. <paramref name="clause"/> names the kind of predicate, for the caller's message.
    /// </summary>
    public static void Validate(string expression, string clause)
    {
        var depth = 0;

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (c is '\'' or '"')
            {
                // Skip to the closing quote. A doubled quote is an escape, so the scan simply
                // continues past it and looks for the next one.
                var quote = c;
                var end = expression.IndexOf(quote, i + 1);
                if (end < 0)
                {
                    throw new SchemaException(
                        "INVALID_EXPRESSION",
                        $"{clause} expression '{expression}' has an unterminated quote.");
                }

                i = end;
                continue;
            }

            switch (c)
            {
                case '(':
                    depth++;
                    break;

                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        throw new SchemaException(
                            "INVALID_EXPRESSION",
                            $"{clause} expression '{expression}' closes a parenthesis it never opened.");
                    }

                    break;

                case ';':
                    throw new SchemaException(
                        "INVALID_EXPRESSION",
                        $"{clause} expression '{expression}' must not contain a statement separator.");

                case '-' when i + 1 < expression.Length && expression[i + 1] == '-':
                case '/' when i + 1 < expression.Length && expression[i + 1] == '*':
                    throw new SchemaException(
                        "INVALID_EXPRESSION",
                        $"{clause} expression '{expression}' must not contain a comment.");

                default:
                    break;
            }
        }

        if (depth != 0)
        {
            throw new SchemaException(
                "INVALID_EXPRESSION",
                $"{clause} expression '{expression}' leaves {depth} parenthesis/parentheses unclosed.");
        }
    }
}
