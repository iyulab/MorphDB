using System.Globalization;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;

namespace MorphDB.Service.OData;

/// <summary>
/// The part of OData's <c>$filter</c> language the service answers, read into a tree and applied to a query as it
/// stands — <c>and</c>, <c>or</c>, <c>not</c> and parentheses over comparisons (<c>eq ne gt ge lt le</c>), <c>in</c> lists
/// and <c>contains</c>, <c>startswith</c> and <c>endswith</c>. A BI tool states a filter as a combination of those, so
/// a filter that may name only one comparison cannot be the surface they are pointed at. What the language reads is
/// listed once, in <see cref="Supported"/>; anything else is refused naming it, never read as less than it says (a
/// dropped condition answers a wider question than the one asked).
/// </summary>
internal static class ODataFilter
{
    /// <summary>What the message of a refused filter says the service reads.</summary>
    public const string Supported =
        "<column> eq|ne|gt|ge|lt|le <value>, <column> in (<value>, …), contains|startswith|endswith(<column>, '<text>'), " +
        "combined with and, or, not and parentheses.";

    /// <summary>Applies the filter to <paramref name="query"/>: every condition of it, in one group, ANDed with what the query already holds.</summary>
    public static IMorphQuery Apply(IMorphQuery query, string filter)
    {
        var node = new Reader(filter).Read();
        return query.WhereGroup(group => Apply(group, node, isOr: false));
    }

    // ---- the tree --------------------------------------------------------------------------------------------------

    private abstract record Node;

    private sealed record Comparison(string Column, FilterOperator Op, object? Value) : Node;

    private sealed record InList(string Column, object[] Values, bool Negated) : Node;

    private sealed record Function(string Name, string Column, string Text) : Node;

    private sealed record Logical(bool IsOr, List<Node> Children) : Node;

    private static IMorphQuery Apply(IMorphQuery query, Node node, bool isOr)
    {
        switch (node)
        {
            case Comparison c:
                return isOr ? query.OrWhere(c.Column, c.Op, c.Value) : query.AndWhere(c.Column, c.Op, c.Value);
            case Function f:
                var op = f.Name switch
                {
                    "contains" => FilterOperator.Contains,
                    "startswith" => FilterOperator.StartsWith,
                    _ => FilterOperator.EndsWith,
                };
                return isOr ? query.OrWhere(f.Column, op, f.Text) : query.AndWhere(f.Column, op, f.Text);
            case InList l:
                IMorphQuery List(IMorphQuery q) => l.Negated ? q.WhereNotIn(l.Column, l.Values) : q.WhereIn(l.Column, l.Values);
                return isOr ? query.OrWhereGroup(List) : List(query);
            case Logical g:
                IMorphQuery Children(IMorphQuery q)
                {
                    for (var i = 0; i < g.Children.Count; i++)
                    {
                        q = Apply(q, g.Children[i], isOr: g.IsOr && i > 0);
                    }

                    return q;
                }

                return isOr ? query.OrWhereGroup(Children) : query.WhereGroup(Children);
            default:
                throw new InvalidOperationException("Unknown filter node.");
        }
    }

    /// <summary>
    /// <c>not</c> pushed to the comparisons it governs, since a query has no negated group: De Morgan over <c>and</c> and <c>or</c>,
    /// the opposite operator for a comparison, the other list operator for <c>in</c>. A function has no opposite the query can state.
    /// </summary>
    private static Node Negate(Node node, string filter) => node switch
    {
        Comparison c => c with { Op = Opposite(c.Op) },
        InList l => l with { Negated = !l.Negated },
        Logical g => new Logical(!g.IsOr, [.. g.Children.Select(child => Negate(child, filter))]),
        _ => throw Refused(filter, "'not' governs a comparison, an 'in' list or a group of them — not contains, startswith or endswith."),
    };

    private static FilterOperator Opposite(FilterOperator op) => op switch
    {
        FilterOperator.Equals => FilterOperator.NotEquals,
        FilterOperator.NotEquals => FilterOperator.Equals,
        FilterOperator.GreaterThan => FilterOperator.LessThanOrEquals,
        FilterOperator.GreaterThanOrEquals => FilterOperator.LessThan,
        FilterOperator.LessThan => FilterOperator.GreaterThanOrEquals,
        _ => FilterOperator.GreaterThan,
    };

    private static ValidationException Refused(string filter, string reason) =>
        new("$filter", $"Unsupported or malformed OData filter expression: '{filter}'. {reason} Supported: {Supported}");

    // ---- reading ---------------------------------------------------------------------------------------------------

    private sealed class Reader
    {
        private readonly string _filter;
        private readonly List<string> _tokens;
        private int _at;

        public Reader(string filter)
        {
            _filter = filter;
            _tokens = Tokenize(filter);
        }

        public Node Read()
        {
            var node = Or();
            if (_at < _tokens.Count)
            {
                throw Refused(_filter, $"'{_tokens[_at]}' was not expected here.");
            }

            return node;
        }

        private Node Or() => Chain(isOr: true, And);

        private Node And() => Chain(isOr: false, Not);

        private Node Chain(bool isOr, Func<Node> operand)
        {
            var first = operand();
            if (!Peek(isOr ? "or" : "and"))
            {
                return first;
            }

            var children = new List<Node> { first };
            while (Peek(isOr ? "or" : "and"))
            {
                _at++;
                children.Add(operand());
            }

            return new Logical(isOr, children);
        }

        private Node Not()
        {
            if (!Peek("not"))
            {
                return Primary();
            }

            _at++;
            return Negate(Not(), _filter);
        }

        private Node Primary()
        {
            var token = Next("an expression");
            if (token == "(")
            {
                var inner = Or();
                Expect(")");
                return inner;
            }

            if (token.ToLowerInvariant() is "contains" or "startswith" or "endswith" && Peek("("))
            {
                var name = token.ToLowerInvariant();
                Expect("(");
                var column = Column();
                Expect(",");
                var text = Literal();
                Expect(")");
                return text is string s ? new Function(name, column, s) : throw Refused(_filter, $"{name}() takes a quoted text as its second argument.");
            }

            var columnName = Column(token);
            var op = Next("an operator").ToLowerInvariant();
            if (op == "in")
            {
                Expect("(");
                var values = new List<object>();
                do
                {
                    values.Add(Literal() ?? throw Refused(_filter, "an 'in' list holds no null — compare with 'eq null'."));
                }
                while (Accept(","));
                Expect(")");
                return new InList(columnName, [.. values], Negated: false);
            }

            FilterOperator comparison = op switch
            {
                "eq" => FilterOperator.Equals,
                "ne" => FilterOperator.NotEquals,
                "gt" => FilterOperator.GreaterThan,
                "ge" => FilterOperator.GreaterThanOrEquals,
                "lt" => FilterOperator.LessThan,
                "le" => FilterOperator.LessThanOrEquals,
                _ => throw Refused(_filter, $"'{op}' is not an operator this service reads."),
            };
            return new Comparison(columnName, comparison, Literal());
        }

        private string Column(string? token = null)
        {
            token ??= Next("a column name");
            return token.Length > 0 && token.All(c => char.IsLetterOrDigit(c) || c == '_') && !char.IsDigit(token[0])
                ? token
                : throw Refused(_filter, $"'{token}' is not a column name.");
        }

        private object? Literal()
        {
            var token = Next("a value");
            if (token.StartsWith('\''))
            {
                return token[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            return token.ToLowerInvariant() switch
            {
                "null" => null,
                "true" => true,
                "false" => false,
                _ when int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
                _ when long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
                _ when decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
                _ when Guid.TryParse(token, out var g) => g,
                // A date or an instant is written bare in OData; the column it is compared with says how it reads.
                _ when token.Length > 0 && char.IsDigit(token[0]) => token,
                _ => throw Refused(_filter, $"'{token}' is not a value."),
            };
        }

        private bool Peek(string word) => _at < _tokens.Count && _tokens[_at].Equals(word, StringComparison.OrdinalIgnoreCase);

        private bool Accept(string word)
        {
            if (!Peek(word))
            {
                return false;
            }

            _at++;
            return true;
        }

        private void Expect(string word)
        {
            if (!Accept(word))
            {
                throw Refused(_filter, _at < _tokens.Count ? $"'{word}' was expected where '{_tokens[_at]}' stands." : $"'{word}' was expected at the end.");
            }
        }

        private string Next(string expected) =>
            _at < _tokens.Count ? _tokens[_at++] : throw Refused(_filter, $"{expected} was expected at the end.");
    }

    /// <summary>Words, quoted texts (<c>''</c> is a quote inside one), and the punctuation <c>( ) ,</c>.</summary>
    private static List<string> Tokenize(string filter)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < filter.Length)
        {
            var c = filter[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '(' or ')' or ',')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (c == '\'')
            {
                var start = i++;
                while (true)
                {
                    if (i >= filter.Length)
                    {
                        throw Refused(filter, "a quoted text is not closed.");
                    }

                    if (filter[i] == '\'')
                    {
                        if (i + 1 < filter.Length && filter[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(filter[start..i]);
            }
            else
            {
                var start = i;
                while (i < filter.Length && !char.IsWhiteSpace(filter[i]) && filter[i] is not ('(' or ')' or ',' or '\''))
                {
                    i++;
                }

                tokens.Add(filter[start..i]);
            }
        }

        return tokens;
    }
}
