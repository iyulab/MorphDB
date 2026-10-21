using System.Text.RegularExpressions;

using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The .NET client against the routes the service serves, both ways. A route the service answers and the
/// client has no method for is invisible to a .NET consumer: they reimplement the call by hand or conclude
/// the feature is missing — index creation sat that way for many releases. A client method that sends a
/// verb and path no route answers is worse: it fails on every call, as an upsert sent with <c>PUT</c> to the
/// single-record path did, because the gates that read paths alone found a route there for another verb.
/// <para>
/// So every call the client makes must name a served route, and each served route is either sent by the
/// client or listed below with why not. The list is held in both directions: a new route the client does not
/// send fails until someone decides, and a listed route the client has since learned to send fails until it
/// is struck off. The client's calls are read from its sources — the verb from the call a path template sits
/// in, the path from the template; a path built into a variable first is matched on its path alone.
/// </para>
/// </summary>
public partial class ClientRouteCoverageTests
{
    /// <summary>Served by the operator surface, deliberately outside the client (and outside the API reference).</summary>
    private static readonly string[] OperatorOnly =
    [
        "GET /api/Diagnostics/pool",
        "GET /api/Diagnostics/queries/slow",
        "GET /api/Diagnostics/queries/stats",
        "GET /api/Diagnostics/shutdown",
        "POST /api/Diagnostics/queries/stats/reset",
    ];

    /// <summary>Served and meant for consumers, but not yet reachable through the client.</summary>
    private static readonly string[] NotYetInClient =
    [
        "GET /api/bulk/export",
        "GET /api/bulk/import",
        "GET /api/bulk/jobs/{jobId}/progress",
        "GET /api/projects/{projectId}/audit/logs",
        "GET /api/projects/{projectId}/audit/logs/{logId}",
        "GET /api/projects/{projectId}/audit/stats",
        "PATCH /api/schema/tables/{name}",
        "GET /api/schema/changelog",
        "GET /api/schema/tables/{name}/history",
        "POST /api/schema/batch",
        "PATCH /api/views/{name}",
        "GET /api/views/{name}/stale",
    ];

    [Fact]
    public void Every_client_call_names_a_served_route()
    {
        var served = Served();
        var ghosts = ReadClient()
            .Where(call => !served.Any(route => call.Sends(route)))
            .Select(call => call.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal);

        string.Join(Environment.NewLine, ghosts).Should().BeEmpty(
            "a client call no route answers fails every time it is made — point it at the route the service serves");
    }

    [Fact]
    public void Every_served_route_is_sent_by_the_client_or_listed_with_why_not()
    {
        var calls = ReadClient();
        var listed = OperatorOnly.Concat(NotYetInClient).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var served = Served();
        var sent = served.Where(route => calls.Any(call => call.Sends(route))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undecided = served
            .Where(r => !listed.Contains(r) && !sent.Contains(r))
            .OrderBy(r => r, StringComparer.Ordinal);
        string.Join(Environment.NewLine, undecided).Should().BeEmpty(
            "a served route the client cannot reach is invisible to a .NET consumer — add a client method, or list it " +
            "here with the reason it stays out");

        var stale = NotYetInClient
            .Where(r => !served.Contains(r, StringComparer.OrdinalIgnoreCase) || sent.Contains(r))
            .OrderBy(r => r, StringComparer.Ordinal);
        string.Join(Environment.NewLine, stale).Should().BeEmpty(
            "a listed route the service no longer serves, or one the client now sends, has to come off the list");
    }

    private static List<string> Served() => DocsRouteParityTests.ServedRoutes()
        .Where(r => r.Contains(" /api/", StringComparison.Ordinal))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>
    /// The calls in the client's sources: every <c>"/api/…"</c> string, query string cut, each interpolation
    /// hole a parameter (holes side by side, <c>{table}{query}</c>, are one; a hole glued to a segment builds a
    /// query string), with the verb of the <c>HttpClient</c> call it is written in when it is written in one.
    /// </summary>
    private static List<ClientCall> ReadClient()
    {
        var directory = Path.GetDirectoryName(ConstraintBoundaryDoc.RepoFilePath("src/MorphDB.Client/SchemaClient.cs"))!;
        var sources = Directory.EnumerateFiles(directory, "*.cs").Select(File.ReadAllText).ToList();
        var literals = sources.SelectMany(text => Literal().Matches(text).Select(m => m.Groups["value"].Value))
            .ToHashSet(StringComparer.Ordinal);
        var calls = sources
            .SelectMany(text => ClientRoute().Matches(text).Select(m =>
            {
                var verb = CallVerb().Match(text[..m.Index]) is { Success: true } call ? call.Groups["verb"].Value.ToUpperInvariant() : null;
                var path = GluedHole().Replace(Hole().Replace(m.Groups["path"].Value.Split('?')[0], "{p}"), "$1");
                return new ClientCall(verb, path.Split('/', StringSplitOptions.RemoveEmptyEntries), literals);
            }))
            .ToList();
        calls.Should().Contain(c => c.Verb == "POST" && c.Segments.Length > 1 && c.Segments[1] == "schema",
            "the client's sources must be found, and their verbs read, for this gate to cover them");
        return calls;
    }

    private sealed record ClientCall(string? Verb, string[] Segments, HashSet<string> Literals)
    {
        /// <summary>
        /// Sends <paramref name="route"/> when the verbs agree (or this call's is not readable) and, segment by
        /// segment, the path has the same literal, a hole for each route parameter, or a hole for a literal the
        /// client passes as a value (an import format).
        /// </summary>
        public bool Sends(string route)
        {
            var space = route.IndexOf(' ', StringComparison.Ordinal);
            if (Verb is not null && !route[..space].Equals(Verb, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var served = route[(space + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            return Segments.Length == served.Length && Segments.Zip(served).All(p =>
                p.Second.StartsWith('{')
                    ? p.First == "{p}"
                    : p.First.Equals(p.Second, StringComparison.OrdinalIgnoreCase) || (p.First == "{p}" && Literals.Contains(p.Second)));
        }

        public override string ToString() => $"{Verb ?? "?"} /{string.Join('/', Segments)}";
    }

    [GeneratedRegex("\"(?<path>/api/[^\"]*)\"")]
    private static partial Regex ClientRoute();

    /// <summary>The <c>HttpClient</c> call a path template is the first argument of: nothing but whitespace and <c>$</c> between.</summary>
    [GeneratedRegex(@"\.(?<verb>Get|Post|Put|Patch|Delete)\w*Async\(\s*\$?$")]
    private static partial Regex CallVerb();

    [GeneratedRegex("\"(?<value>[a-z]+)\"")]
    private static partial Regex Literal();

    [GeneratedRegex(@"(\{[^{}]*(\{[^{}]*\}[^{}]*)*\})+")]
    private static partial Regex Hole();

    /// <summary>A hole glued to the end of a literal segment builds a query string (<c>data{query}</c>), not a segment.</summary>
    [GeneratedRegex(@"([A-Za-z0-9_-])\{p\}")]
    private static partial Regex GluedHole();
}
