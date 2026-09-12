using System.Text.RegularExpressions;
using MorphDB.Service.Realtime;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// A hub event the service declares but never sends is a contract that reads as supported and is
/// not: the interface compiles, the documentation describes it, a client registers a handler for it,
/// and nothing ever arrives. Four separate defects of that shape have been found by hand. The sister
/// gate in <see cref="RealtimeClientEventDriftTests"/> relates the client's event literals to this
/// interface; it cannot see whether anything on the server side ever calls them, because an
/// interface method with no caller compiles perfectly. This gate closes that half: every method
/// <see cref="IMorphHubClient"/> declares must have at least one publishing call site in the
/// service.
/// </summary>
public partial class HubEventPublisherGateTests
{
    private const string ServiceAnchor = "src/MorphDB.Service/Realtime/MorphHub.cs";

    [Fact]
    public void Every_event_the_hub_declares_is_published_somewhere_in_the_service()
    {
        var published = PublishedEvents(ServiceSources());
        var declared = DeclaredEvents();

        published.Should().NotBeEmpty("the gate is vacuous over a service that publishes nothing");
        declared.Should().BeSubsetOf(published,
            $"a method {nameof(IMorphHubClient)} declares with no `Clients….X(…)` call site is an "
            + "event the server can never send — subscribers wait for it forever and the compiler "
            + "reports nothing");
    }

    [Fact]
    public void The_gate_reports_a_declared_event_that_nothing_publishes()
    {
        const string oneEventLost = """
            await Clients.Caller.Subscribed(tableName);
            await _hubContext.Clients.Group(groupName).RecordCreated(message);
            """;

        var published = PublishedEvents([oneEventLost]);

        published.Should().BeEquivalentTo(["Subscribed", "RecordCreated"]);
        DeclaredEvents().Should().NotBeSubsetOf(published,
            "without this the subset check above would pass over a service that had lost a "
            + "publisher, and the gate would be decoration");
    }

    [Fact]
    public void The_gate_reads_both_shapes_of_publishing_call_site()
    {
        const string bothShapes = """
            await Clients.Caller.Unsubscribed(tableName);
            await _hubContext.Clients.Group(groupName).RecordDeleted(message);
            """;

        PublishedEvents([bothShapes]).Should().BeEquivalentTo(["Unsubscribed", "RecordDeleted"],
            "the hub sends to the caller directly and the listener sends to a group through the hub "
            + "context; a gate that reads only one of the two would go green on a service that had "
            + "lost every publisher of the other kind");
    }

    private static IReadOnlyList<string> DeclaredEvents() =>
        [.. typeof(IMorphHubClient).GetMethods().Select(method => method.Name)];

    private static IReadOnlyList<string> ServiceSources()
    {
        var realtimeDirectory = Path.GetDirectoryName(ConstraintBoundaryDoc.RepoFilePath(ServiceAnchor))!;
        var serviceDirectory = Path.GetDirectoryName(realtimeDirectory)!;

        return
        [
            .. Directory.EnumerateFiles(serviceDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path, serviceDirectory))
                .Select(File.ReadAllText)
        ];
    }

    // The build drops generated sources under bin/ and obj/, and a stale copy of a deleted publisher
    // lives there long after the source is gone — reading them would let this gate pass on a
    // publisher that no longer exists.
    private static bool IsBuildOutput(string path, string root) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "bin" or "obj");

    private static IReadOnlyList<string> PublishedEvents(IEnumerable<string> sources) =>
        [.. sources
            .SelectMany(source => PublishCall().Matches(source).AsEnumerable())
            .Select(match => match.Groups["event"].Value)
            .Distinct(StringComparer.Ordinal)];

    // `Clients.Caller.X(`, `Clients.All.X(`, `Clients.Group("…").X(` — the receiver selector may or
    // may not take arguments, and the event is whatever is invoked on what it returns.
    [GeneratedRegex(
        @"Clients\s*\.\s*[A-Za-z_][A-Za-z0-9_]*\s*(?:\([^()]*\))?\s*\.\s*(?<event>[A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex PublishCall();
}
