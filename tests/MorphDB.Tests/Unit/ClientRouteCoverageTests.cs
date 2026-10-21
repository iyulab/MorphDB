using System.Text.RegularExpressions;

using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Which served routes the .NET client can reach. A route the service answers and the client has no
/// method for is invisible to a .NET consumer: they reimplement the call by hand or conclude the
/// feature is missing. Index creation sat in that state for many releases with nothing noticing,
/// because every other gate here asks whether a documented or a client route exists on the server —
/// none asks the reverse.
/// <para>
/// Each served route is either reached by a path the client builds, or listed below with why it is not.
/// The list is held in both directions: a new route the client does not reach fails until someone
/// decides, and a listed route the client has since learned to reach fails until it is struck off.
/// Matching is by path, not verb — the client's paths are string templates beside the call that sends
/// them, so the verb is not readable from the text; a route sharing its path with another verb the client
/// does send stays listed, and is not reported stale, while it has no method of its own.
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
        "PUT /api/batch/data/{table}",
        "PATCH /api/batch/data/{table}",
        "DELETE /api/batch/data/{table}",
        "POST /api/batch/data/{table}/seed",
        "POST /api/data/{table}/query",
        "GET /api/bulk/export",
        "GET /api/bulk/import",
        "GET /api/bulk/jobs/{jobId}/progress",
        "GET /api/hierarchy/{table}/detect-cycles",
        "POST /api/hierarchy/{table}/check-cycle",
        "GET /api/hierarchy/{table}/{id}/ancestors",
        "GET /api/hierarchy/{table}/{id}/descendants",
        "GET /api/hierarchy/{table}/{id}/path",
        "GET /api/hierarchy/{table}/{id}/siblings",
        "GET /api/hierarchy/{table}/{id}/subtree",
        "GET /api/projects/{projectId}/audit/logs",
        "GET /api/projects/{projectId}/audit/logs/{logId}",
        "GET /api/projects/{projectId}/audit/stats",
        "GET /api/schema/changelog",
        "GET /api/schema/tables/{name}/history",
        "POST /api/schema/batch",
        "GET /api/security/policies/{tableName}",
        "POST /api/security/policies",
        "PATCH /api/security/policies/{policyId}",
        "DELETE /api/security/policies/{policyId}",
        "GET /api/security/secrets",
        "POST /api/security/secrets",
        "DELETE /api/security/secrets/{secretId}",
        "GET /api/views/{name}/stale",
    ];

    [Fact]
    public void Every_served_route_is_reached_by_the_client_or_listed_with_why_not()
    {
        var client = ReadClient();
        var listed = OperatorOnly.Concat(NotYetInClient).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var served = DocsRouteParityTests.ServedRoutes()
            .Where(r => r.Contains(" /api/", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reached = served.Where(r => client.Reaches(PathOf(r))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var undecided = served
            .Where(r => !listed.Contains(r) && !reached.Contains(r))
            .OrderBy(r => r, StringComparer.Ordinal);
        string.Join(Environment.NewLine, undecided).Should().BeEmpty(
            "a served route the client cannot reach is invisible to a .NET consumer — add a client method, or list it " +
            "here with the reason it stays out");

        var stale = NotYetInClient
            .Where(r => !served.Contains(r, StringComparer.OrdinalIgnoreCase)
                || (reached.Contains(r) && !served.Any(s => !s.Equals(r, StringComparison.OrdinalIgnoreCase)
                    && PathOf(s).Equals(PathOf(r), StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => r, StringComparer.Ordinal);
        string.Join(Environment.NewLine, stale).Should().BeEmpty(
            "a listed route the service no longer serves, or one the client now reaches, has to come off the list");
    }

    private static string PathOf(string route) => route[(route.IndexOf(' ', StringComparison.Ordinal) + 1)..];

    /// <summary>
    /// The client's sources, read as text: the paths they build — every <c>"/api/…"</c> string, query string cut,
    /// each interpolation hole a parameter (holes side by side, <c>{table}{query}</c>, are one) — and the string
    /// literals they pass, which is how a hole comes to stand for a fixed segment (an import format).
    /// </summary>
    private static ClientText ReadClient()
    {
        var directory = Path.GetDirectoryName(ConstraintBoundaryDoc.RepoFilePath("src/MorphDB.Client/SchemaClient.cs"))!;
        var sources = Directory.EnumerateFiles(directory, "*.cs").Select(File.ReadAllText).ToList();
        var paths = sources
            .SelectMany(text => ClientRoute().Matches(text).Select(m => m.Groups["path"].Value))
            .Select(path => GluedHole().Replace(Hole().Replace(path.Split('?')[0], "{p}"), "$1"))
            .Select(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .ToList();
        paths.Should().Contain(p => p.Length > 1 && p[1] == "schema", "the client's sources must be found for this gate to cover them");
        var literals = sources.SelectMany(text => Literal().Matches(text).Select(m => m.Groups["value"].Value))
            .ToHashSet(StringComparer.Ordinal);
        return new ClientText(paths, literals);
    }

    private sealed record ClientText(List<string[]> Paths, HashSet<string> Literals)
    {
        /// <summary>
        /// A served template is reached when some client path has as many segments and, segment by segment, the
        /// same literal, a hole for each route parameter, or a hole for a literal the client passes as a value.
        /// </summary>
        public bool Reaches(string template)
        {
            var served = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return Paths.Any(path => path.Length == served.Length && path.Zip(served).All(p =>
                p.Second.StartsWith('{')
                    ? p.First == "{p}"
                    : p.First.Equals(p.Second, StringComparison.OrdinalIgnoreCase) || (p.First == "{p}" && Literals.Contains(p.Second))));
        }
    }

    [GeneratedRegex("\"(?<path>/api/[^\"]*)\"")]
    private static partial Regex ClientRoute();

    [GeneratedRegex(@"(\{[^{}]*(\{[^{}]*\}[^{}]*)*\})+")]
    private static partial Regex Hole();

    [GeneratedRegex("\"(?<value>[a-z]+)\"")]
    private static partial Regex Literal();

    /// <summary>A hole glued to the end of a literal segment builds a query string (<c>data{query}</c>), not a segment.</summary>
    [GeneratedRegex(@"([A-Za-z0-9_-])\{p\}")]
    private static partial Regex GluedHole();
}
