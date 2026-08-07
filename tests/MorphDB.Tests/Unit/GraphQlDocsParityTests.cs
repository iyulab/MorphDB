using System.Text.RegularExpressions;
using HotChocolate.Execution;
using HotChocolate.Language;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.GraphQL;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for GraphQL — the sibling of <see cref="DocsRouteParityTests"/>,
/// and it exists for the same reason: a ghost contract in the docs is an active defect, not a
/// cosmetic one.
/// <para>
/// The route gate reads <c>/api</c> only, and says so in a comment, because the other protocols
/// are served by their own providers rather than by controllers. That comment described a real
/// limit and left this surface uncovered: the schema tests next door
/// (<see cref="GraphQlSchemaContractTests"/>, <see cref="ServedSchemaSnapshotTests"/>) hold the
/// server to what it served yesterday, which is a different question from whether
/// <c>docs/API.md</c> describes the server at all. Both can pass while the documentation shows
/// operations no schema ever had.
/// </para>
/// <para>
/// Both directions are enforced here, unlike the route gate, and the reason is specific to this
/// protocol: introspection is part of GraphQL's default security posture and is refused outside
/// the Development environment, so a client running against a released image cannot ask the
/// server what it serves. The documentation is the only schema reference those clients have. An
/// operation missing from it is not merely undocumented — it is undiscoverable.
/// </para>
/// <para>
/// Neither side is a hand-kept list. The served side is the schema's own SDL, and the documented
/// side is every GraphQL block in <c>docs/API.md</c> parsed by the same parser that reads client
/// requests — so a root field added later fails this gate until it is written down, and an
/// example that stops parsing fails it immediately.
/// </para>
/// </summary>
public partial class GraphQlDocsParityTests
{
    [Fact]
    public async Task Every_documented_operation_is_one_the_served_schema_can_answer()
    {
        var schema = await ServedSchemaAsync();
        var complaints = new List<string>();

        foreach (var (block, document) in DocumentedBlocks())
        {
            var fragments = document.Definitions.OfType<FragmentDefinitionNode>()
                .ToDictionary(f => f.Name.Value, StringComparer.Ordinal);

            foreach (var operation in document.Definitions.OfType<OperationDefinitionNode>())
            {
                var rootType = schema.RootTypeFor(operation.Operation);

                if (rootType is null)
                {
                    complaints.Add($"{block}: the schema serves no {operation.Operation} root type");
                    continue;
                }

                Walk(schema, fragments, rootType, operation.SelectionSet, $"{block} {rootType}", complaints);
            }
        }

        // Joined rather than asserted as a collection so a failure names every drift at once —
        // fixing them one run at a time is how a sweep misses instances.
        string.Join(Environment.NewLine, complaints).Should().BeEmpty(
            "an operation in API.md the schema cannot answer is a ghost contract — a reader who "
            + "copies it gets an error from the server. Fix whichever side drifted: correct the "
            + "example, or serve what it already promises.");
    }

    [Fact]
    public async Task Every_operation_the_schema_serves_is_written_down()
    {
        var schema = await ServedSchemaAsync();
        var documented = DocumentedRootFields();
        var missing = new List<string>();

        foreach (var operation in new[] { OperationType.Query, OperationType.Mutation, OperationType.Subscription })
        {
            var rootType = schema.RootTypeFor(operation);
            if (rootType is null)
            {
                continue;
            }

            missing.AddRange(schema.Fields(rootType)
                .Keys
                .Where(field => !documented.Contains((operation, field)))
                .Select(field => $"{operation.ToString().ToLowerInvariant()} {{ {field} }}"));
        }

        string.Join(Environment.NewLine, missing.OrderBy(m => m, StringComparer.Ordinal)).Should().BeEmpty(
            "introspection is refused outside Development, so API.md is the only schema reference a "
            + "client of a released build has. A root field absent from it cannot be discovered at all.");
    }

    [Fact]
    public async Task Introspection_stays_shut_so_the_documentation_is_the_only_reference()
    {
        // The premise the gate above rests on, held rather than assumed. Nothing in this repository
        // turns introspection off: it is the server's own default outside Development, and a version
        // bump that reopened it -- or a configuration change that closed it in Development too --
        // would silently change what API.md has to carry. The published image runs Production.
        var executor = await new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .BuildRequestExecutorAsync();

        var result = (await executor.ExecuteAsync("{ __schema { queryType { name } } }"))
            .ExpectOperationResult();

        result.Errors.Should().ContainSingle(
            "a released deployment answers introspection with a refusal, which is why API.md is "
            + "written as the schema reference rather than as a tour of it")
            .Which.Code.Should().Be("HC0046");
    }

    /// <summary>
    /// Checks one selection set against the type it is selected on, descending into sub-selections.
    /// </summary>
    private static void Walk(
        ServedSchema schema,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        string typeName,
        SelectionSetNode selectionSet,
        string path,
        List<string> complaints)
    {
        var fields = schema.Fields(typeName);

        foreach (var selection in selectionSet.Selections)
        {
            switch (selection)
            {
                case FieldNode field when field.Name.Value.StartsWith("__", StringComparison.Ordinal):
                    // Introspection meta-fields belong to every type and to no schema document.
                    break;

                case FieldNode field when !fields.TryGetValue(field.Name.Value, out _):
                    complaints.Add($"{path}: no field '{field.Name.Value}'");
                    break;

                case FieldNode field:
                    WalkField(schema, fragments, fields[field.Name.Value], field, $"{path}.{field.Name.Value}", complaints);
                    break;

                case InlineFragmentNode inline:
                    Walk(schema, fragments, inline.TypeCondition?.Name.Value ?? typeName, inline.SelectionSet, path, complaints);
                    break;

                case FragmentSpreadNode spread when fragments.TryGetValue(spread.Name.Value, out var fragment):
                    Walk(schema, fragments, fragment.TypeCondition.Name.Value, fragment.SelectionSet, path, complaints);
                    break;

                case FragmentSpreadNode spread:
                    complaints.Add($"{path}: fragment '{spread.Name.Value}' is used but not defined in the same example");
                    break;
            }
        }
    }

    private static void WalkField(
        ServedSchema schema,
        IReadOnlyDictionary<string, FragmentDefinitionNode> fragments,
        FieldDefinitionNode definition,
        FieldNode usage,
        string path,
        List<string> complaints)
    {
        var declared = definition.Arguments.Select(a => a.Name.Value).ToHashSet(StringComparer.Ordinal);

        complaints.AddRange(usage.Arguments
            .Select(a => a.Name.Value)
            .Where(name => !declared.Contains(name))
            .Select(name => $"{path}: no argument '{name}'"));

        if (usage.SelectionSet is null)
        {
            return;
        }

        var named = NamedTypeOf(definition.Type);

        // A scalar carries no sub-selection. Reporting it here rather than letting the recursion
        // find an empty field set names the mistake the reader actually made.
        if (schema.IsLeaf(named))
        {
            complaints.Add($"{path}: '{named}' is a leaf type and takes no sub-selection");
            return;
        }

        Walk(schema, fragments, named, usage.SelectionSet, path, complaints);
    }

    private static string NamedTypeOf(ITypeNode type) => type switch
    {
        NonNullTypeNode nonNull => NamedTypeOf(nonNull.Type),
        ListTypeNode list => NamedTypeOf(list.Type),
        NamedTypeNode named => named.Name.Value,
        _ => type.ToString()
    };

    /// <summary>
    /// The root field each documented example calls, keyed by the operation it belongs to. Only
    /// the top level: nested fields are checked by the other direction.
    /// </summary>
    private static HashSet<(OperationType, string)> DocumentedRootFields()
        => DocumentedBlocks()
            .SelectMany(b => b.Document.Definitions.OfType<OperationDefinitionNode>())
            .SelectMany(o => o.SelectionSet.Selections.OfType<FieldNode>().Select(f => (o.Operation, f.Name.Value)))
            .ToHashSet();

    /// <summary>
    /// Every fenced GraphQL example in <c>docs/API.md</c>, parsed. Parsing here is itself part of
    /// the gate: an example that no parser accepts is one no client can send.
    /// </summary>
    private static IReadOnlyList<(string Block, DocumentNode Document)> DocumentedBlocks()
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

        blocks.Should().NotBeEmpty("API.md must carry GraphQL examples for this gate to mean anything");

        return blocks;
    }

    private static async Task<ServedSchema> ServedSchemaAsync()
    {
        // The shape comes from CLR types and resolvers read metadata per request, so no database
        // is involved -- the same reason the schema tests next door are unit tests.
        var schema = await new ServiceCollection()
            .AddGraphQLServer()
            .AddMorphDbTypes()
            .BuildSchemaAsync();

        return new ServedSchema(Utf8GraphQLParser.Parse(schema.ToString()));
    }

    /// <summary>
    /// The served schema read as a document, which is the form a client would introspect if it
    /// could. Asking the schema for its own SDL is what keeps this gate from needing a list of
    /// types someone remembered to add.
    /// </summary>
    private sealed class ServedSchema
    {
        private readonly Dictionary<string, Dictionary<string, FieldDefinitionNode>> _fields;
        private readonly Dictionary<OperationType, string> _roots;
        private readonly HashSet<string> _leaves;

        public ServedSchema(DocumentNode sdl)
        {
            _fields = sdl.Definitions.OfType<ObjectTypeDefinitionNode>()
                .Select(t => (Name: t.Name.Value, t.Fields))
                .Concat(sdl.Definitions.OfType<InterfaceTypeDefinitionNode>()
                    .Select(t => (Name: t.Name.Value, t.Fields)))
                .ToDictionary(
                    t => t.Name,
                    t => t.Fields.ToDictionary(f => f.Name.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal);

            _leaves = sdl.Definitions.OfType<ScalarTypeDefinitionNode>().Select(s => s.Name.Value)
                .Concat(sdl.Definitions.OfType<EnumTypeDefinitionNode>().Select(e => e.Name.Value))
                .Concat(["String", "Int", "Float", "Boolean", "ID"])
                .ToHashSet(StringComparer.Ordinal);

            // Root types are read from the schema definition rather than assumed to be called
            // Query/Mutation/Subscription -- renaming one is exactly the kind of move a contract
            // gate should follow rather than be broken by.
            _roots = sdl.Definitions.OfType<SchemaDefinitionNode>()
                .SelectMany(s => s.OperationTypes)
                .ToDictionary(o => o.Operation, o => o.Type.Name.Value);
        }

        public string? RootTypeFor(OperationType operation)
            => _roots.TryGetValue(operation, out var name) ? name : null;

        public IReadOnlyDictionary<string, FieldDefinitionNode> Fields(string typeName)
            => _fields.TryGetValue(typeName, out var fields)
                ? fields
                : new Dictionary<string, FieldDefinitionNode>(StringComparer.Ordinal);

        public bool IsLeaf(string typeName) => _leaves.Contains(typeName);
    }

    [GeneratedRegex(@"```graphql\r?\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex GraphQlBlock();
}
