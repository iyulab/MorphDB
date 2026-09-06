using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using MorphDB.Service.Realtime;
using MorphDB.Tests.Fixtures;

// Moq is in scope here through the test project's global usings and also defines a `Match`.
using RegexMatch = System.Text.RegularExpressions.Match;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for the real-time hub — the third of the protocols the route gate
/// cannot reach, after <see cref="GraphQlDocsParityTests"/> and <see cref="ODataDocsParityTests"/>.
/// <para>
/// This surface needs it most. A GraphQL client can introspect where introspection is open, and an
/// OData client can read <c>$metadata</c>; a SignalR client has no schema document at all, in any
/// environment. The documentation is the only description of the hub that exists, and it described
/// a different hub: an event named <c>DataChanged</c> that appears nowhere in the source, an
/// <c>operation</c> in lower case, and a before-image no message carries. A client written from it
/// connects, subscribes, and then silently receives nothing it recognises.
/// </para>
/// <para>
/// Both directions are enforced, for that reason. The server side is read by reflection — the
/// methods on <see cref="MorphHub"/>, the callbacks on <see cref="IMorphHubClient"/>, and for each
/// callback the properties of the message it carries — so an event added later fails this gate
/// until it is written down.
/// </para>
/// </summary>
public partial class RealtimeDocsParityTests
{
    [Fact]
    public void The_documented_hub_methods_are_the_ones_the_hub_has()
    {
        // Declared here and not an override: the connection lifecycle callbacks are the hub's own
        // methods too, but they answer the framework rather than a client's invoke.
        var served = typeof(MorphHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == typeof(MorphHub))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        DocumentedRows("Hub methods").Keys.Should().BeEquivalentTo(served,
            "a client can only reach the hub by naming a method, and there is no document it could "
            + "read instead of this one — a name that drifted either way is unreachable surface");
    }

    [Fact]
    public void The_documented_events_are_the_ones_the_hub_sends()
    {
        var served = typeof(IMorphHubClient).GetMethods().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        DocumentedRows("Client events").Keys.Should().BeEquivalentTo(served,
            "an event the server sends and the documentation omits is one no client subscribes to, "
            + "and an event only the documentation has is a callback that never fires");
    }

    [Fact]
    public void Every_documented_event_carries_the_fields_its_message_actually_has()
    {
        var documented = DocumentedRows("Client events");
        var complaints = new List<string>();

        foreach (var callback in typeof(IMorphHubClient).GetMethods())
        {
            if (!documented.TryGetValue(callback.Name, out var fields))
            {
                continue; // Named by the fact above; nothing to add here.
            }

            var expected = PayloadFieldsOf(callback);

            if (!fields.OrderBy(f => f, StringComparer.Ordinal)
                    .SequenceEqual(expected.OrderBy(f => f, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                complaints.Add(
                    $"{callback.Name}: documented [{string.Join(' ', fields)}] but sends [{string.Join(' ', expected)}]");
            }
        }

        string.Join(Environment.NewLine, complaints).Should().BeEmpty(
            "the payload is only ever seen at runtime, so a field named here that never arrives — or "
            + "one that arrives and is not named — is a defect no client can discover except by "
            + "reading a live stream");
    }

    [Fact]
    public void The_documented_operation_values_are_the_ones_a_change_event_carries()
    {
        var sentence = OperationSentence().Match(DocsText());
        sentence.Success.Should().BeTrue("the real-time section must state what `operation` carries");

        var documented = Token().Matches(sentence.Groups["values"].Value)
            .Select(m => m.Groups["token"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        documented.Should().NotBeEmpty("the real-time section must say what operation carries");

        documented.Should().BeSubsetOf(ChangeOperation.All,
            "a client branches on this value, and it is written by the listener rather than passed "
            + "through — the documented spelling has to be the one that arrives, case included");
    }

    /// <summary>
    /// The example is the first thing a real-time consumer runs, and it is executable text: the
    /// number of arguments it passes is a claim about the hub, not prose about it. SignalR binds an
    /// invocation by argument count alone, so an example one argument short of its target is
    /// refused at the binder — and a C# default parameter does not close the gap, because no wire
    /// protocol carries one. That is exactly how the documented `Subscribe` call came to fail for
    /// every client that followed it while every other gate here stayed green: they compare names,
    /// and the names were right.
    /// </summary>
    [Fact]
    public void The_documented_example_calls_each_hub_method_with_the_arguments_it_binds()
    {
        var complaints = new List<string>();

        foreach (RegexMatch invocation in ExampleInvocation().Matches(DocsText()))
        {
            var method = invocation.Groups["method"].Value;

            // Everything after the method name, comma-separated. The example's calls pass literals,
            // so counting separators is enough and keeps the gate from needing a JS parser.
            var arguments = invocation.Groups["args"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;

            var bound = typeof(MorphHub).GetMethod(method)?.GetParameters().Length;

            if (bound is null)
            {
                complaints.Add($"{method}: the example invokes a method the hub does not have");
            }
            else if (bound != arguments)
            {
                complaints.Add($"{method}: the example passes {arguments} argument(s), the hub binds {bound}");
            }
        }

        string.Join(Environment.NewLine, complaints).Should().BeEmpty(
            "a reader runs the example as written, and SignalR refuses an invocation whose argument "
            + "count does not match the target — an example that is one short fails before the hub "
            + "method is ever entered");
    }

    /// <summary>
    /// The field names a callback's payload arrives with. A message class contributes its
    /// properties; a callback that takes a bare value contributes the parameter's own name. Both
    /// are camel-cased, which is what the JSON hub protocol puts on the wire.
    /// </summary>
    private static IReadOnlyList<string> PayloadFieldsOf(MethodInfo callback)
    {
        var parameter = callback.GetParameters().Single();

        return parameter.ParameterType.Assembly == typeof(MorphHub).Assembly
            ? parameter.ParameterType.GetProperties().Select(p => CamelCase(p.Name)).ToList()
            : [CamelCase(parameter.Name!)];
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string DocsText() => ConstraintBoundaryDoc.ReadRepoFile("docs/API.md");

    /// <summary>
    /// Reads a two-column table under the named heading into "first cell -> tokens in the second".
    /// The tables are the documentation's own statement of the surface, which is why they are what
    /// the gate reads rather than the prose around them.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> DocumentedRows(string heading)
    {
        var section = SectionUnder(heading).Match(DocsText());
        section.Success.Should().BeTrue($"API.md must carry a '{heading}' table");

        var rows = TableRow().Matches(section.Groups["body"].Value)
            .ToDictionary(
                m => m.Groups["name"].Value,
                m => (IReadOnlyList<string>)Token().Matches(m.Groups["rest"].Value)
                    .Select(t => t.Groups["token"].Value).ToList(),
                StringComparer.Ordinal);

        rows.Should().NotBeEmpty($"the '{heading}' table must have rows");

        return rows;
    }

    private static Regex SectionUnder(string heading)
        => new($@"####\s+{Regex.Escape(heading)}\r?\n(?<body>.*?)(?=\r?\n####|\r?\n##\s|\z)", RegexOptions.Singleline);

    /// <summary>A row whose first cell is a single backticked name; the header row has none.</summary>
    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z]+)`\s*\|(?<rest>[^\r\n]*)\|", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"`(?<token>[A-Za-z]+)`")]
    private static partial Regex Token();

    /// <summary>A `connection.invoke("Method", …)` line from the JavaScript example.</summary>
    [GeneratedRegex(@"invoke\(\s*""(?<method>[A-Za-z]+)""(?<args>[^)]*)\)")]
    private static partial Regex ExampleInvocation();

    /// <summary>The clause that states the operation vocabulary, up to the dash that explains it.</summary>
    [GeneratedRegex(@"`operation` is (?<values>[^—\r\n]*)")]
    private static partial Regex OperationSentence();
}
