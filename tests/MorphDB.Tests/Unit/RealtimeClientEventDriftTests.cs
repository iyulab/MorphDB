using System.Text.RegularExpressions;
using MorphDB.Service.Realtime;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The supported .NET client used to listen for a <c>ReceiveChange</c> event the hub had never sent.
/// The event names a SignalR client registers are string literals, the hub's are method names on
/// <see cref="IMorphHubClient"/>, and nothing in the compiler relates the two — a renamed hub event
/// leaves the client compiling and deaf. This gate relates them: every event literal the client
/// registers must be a method the hub's client interface declares, and every change event the hub
/// declares must be one the client registers.
/// </summary>
public partial class RealtimeClientEventDriftTests
{
    private const string ClientRelativePath = "src/MorphDB.Client/RealtimeClient.cs";

    [Fact]
    public void Every_event_the_client_registers_is_one_the_hub_declares()
    {
        var registered = RegisteredEvents(ConstraintBoundaryDoc.ReadRepoFile(ClientRelativePath));

        registered.Should().NotBeEmpty("the gate is vacuous over a client that registers nothing");
        registered.Should().BeSubsetOf(HubEvents(),
            $"{ClientRelativePath} registers a handler for an event {nameof(IMorphHubClient)} does not "
            + "declare, which the server will therefore never send — this is exactly how the client "
            + "went deaf the first time");
    }

    [Fact]
    public void Every_change_event_the_hub_declares_is_one_the_client_registers()
    {
        var registered = RegisteredEvents(ConstraintBoundaryDoc.ReadRepoFile(ClientRelativePath));
        var changeEvents = HubEvents().Where(name => name.StartsWith("Record", StringComparison.Ordinal));

        changeEvents.Should().BeSubsetOf(registered,
            "a change event the hub broadcasts and the client does not listen for is a change the "
            + "subscriber silently never sees");
    }

    [Fact]
    public void The_gate_reports_a_client_registering_an_event_the_hub_does_not_declare()
    {
        const string reintroduced = "_connection.On<string, string, string>(\"ReceiveChange\", (t, o, d) =>";

        var registered = RegisteredEvents(reintroduced);

        registered.Should().ContainSingle().Which.Should().Be("ReceiveChange");
        HubEvents().Should().NotContain("ReceiveChange",
            "without this the subset check above would pass over the original defect and be vacuous");
    }

    private static IReadOnlyList<string> HubEvents() =>
        [.. typeof(IMorphHubClient).GetMethods().Select(method => method.Name)];

    private static IReadOnlyList<string> RegisteredEvents(string clientSource) =>
        [.. OnRegistration().Matches(clientSource).Select(match => match.Groups["event"].Value)];

    [GeneratedRegex(@"\.On<[^>]+>\(\s*""(?<event>[A-Za-z_][A-Za-z0-9_]*)""")]
    private static partial Regex OnRegistration();
}
