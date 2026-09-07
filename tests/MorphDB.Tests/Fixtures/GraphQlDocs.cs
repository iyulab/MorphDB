using System.Text.RegularExpressions;
using HotChocolate.Language;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Every fenced GraphQL example in <c>docs/API.md</c>, parsed. Shared rather than private to one
/// gate: two of them read the same examples and ask different questions of them (do the names match
/// the schema, does the server's validator accept the operation), and a second copy of the reader is
/// a second place for "which blocks count" to be changed — after which the two gates disagree about
/// what the document contains.
/// <para>
/// Parsing here is itself part of both gates: an example no parser accepts is one no client can
/// send, so a syntax error surfaces as a failure rather than a skipped block.
/// </para>
/// </summary>
internal static partial class GraphQlDocs
{
    public static IReadOnlyList<(string Label, DocumentNode Document)> Blocks()
    {
        var markdown = ConstraintBoundaryDoc.ReadRepoFile("docs/API.md");
        var blocks = new List<(string, DocumentNode)>();
        var ordinal = 0;

        foreach (System.Text.RegularExpressions.Match match in GraphQlBlock().Matches(markdown))
        {
            ordinal++;
            var label = $"API.md graphql example #{ordinal}";
            var source = match.Groups["body"].Value;

            try
            {
                blocks.Add((label, Utf8GraphQLParser.Parse(source)));
            }
            catch (SyntaxException ex)
            {
                throw new InvalidOperationException($"{label} does not parse as GraphQL: {ex.Message}", ex);
            }
        }

        blocks.Should().NotBeEmpty("API.md must carry GraphQL examples for these gates to mean anything");

        return blocks;
    }

    [GeneratedRegex(@"```graphql\r?\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex GraphQlBlock();
}
