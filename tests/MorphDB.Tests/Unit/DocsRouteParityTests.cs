using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for routes — the sibling of <see cref="DocsErrorCodeParityTests"/>,
/// and it exists for the same reason: a ghost contract in the docs is an active defect, not a
/// cosmetic one. A consumer does not discover that <c>docs/API.md</c> promises an endpoint the
/// server never had until they get a 404 in their own integration.
/// <para>
/// Unlike the error-code gate this needs no hand-audited inventory: the server side is read by
/// reflection over the controllers, so it cannot fall out of date on its own. Only one direction
/// is enforced — every documented route must exist. The reverse (every route documented) is a
/// product decision about what belongs in the public surface, and diagnostics endpoints are
/// deliberately not in <c>API.md</c>.
/// </para>
/// </summary>
public partial class DocsRouteParityTests
{
    [Fact]
    public void Every_documented_route_exists_on_a_controller()
    {
        var served = ServedRoutes();
        var documented = DocumentedRoutes();

        documented.Should().NotBeEmpty("API.md must carry routes for this gate to mean anything");

        var ghosts = documented
            .Where(d => !served.Any(s => Matches(d, s)))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        // Joined rather than asserted as a collection so a failure names every ghost at once —
        // fixing them one run at a time is how a sweep misses instances.
        string.Join(Environment.NewLine, ghosts).Should().BeEmpty(
            "a route in API.md that no controller answers is a ghost contract — consumers who follow " +
            "the docs get a 404. Fix whichever side drifted: correct the doc, or add the endpoint it " +
            "already promises.");
    }

    /// <summary>
    /// A documented route matches a served one when their segments line up, treating any route
    /// parameter on the server side as a wildcard — the docs legitimately show concrete examples
    /// (<c>/api/data/customers</c>) where the template carries a parameter (<c>/api/data/{table}</c>).
    /// </summary>
    private static bool Matches(string documented, string served)
    {
        var left = documented.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var right = served.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (left.Length != right.Length)
        {
            return false;
        }

        return !left.Where((segment, i) =>
            !right[i].StartsWith('{') && !segment.Equals(right[i], StringComparison.OrdinalIgnoreCase)).Any();
    }

    private static IReadOnlyList<string> ServedRoutes()
    {
        var controllers = typeof(MorphDB.Service.GraphQL.DynamicSchemaBuilder).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var routes = new List<string>();

        foreach (var controller in controllers)
        {
            var prefix = (controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty)
                .Replace("[controller]", ControllerToken(controller), StringComparison.Ordinal);

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var verb in method.GetCustomAttributes().OfType<HttpMethodAttribute>())
                {
                    var template = string.Join('/', new[] { prefix, verb.Template ?? string.Empty }
                        .Where(part => part.Length > 0));

                    routes.Add($"{verb.HttpMethods.First().ToUpperInvariant()} /{StripConstraints(template)}");
                }
            }
        }

        return routes;
    }

    private static string ControllerToken(Type controller)
        => controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;

    /// <summary>Drops route constraints (<c>{id:guid}</c>) — they bind values, not shapes.</summary>
    private static string StripConstraints(string template)
        => RouteConstraint().Replace(template, "{$1}");

    private static IReadOnlyList<string> DocumentedRoutes()
        => DocumentedRoute()
            .Matches(File.ReadAllText(FindDocsFile("API.md")))
            .Select(m => $"{m.Groups["verb"].Value} {m.Groups["path"].Value.TrimEnd('/')}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FindDocsFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"docs/{name} not found above {AppContext.BaseDirectory}");
    }

    [GeneratedRegex(@"\{([A-Za-z0-9_]+):[^}]+\}")]
    private static partial Regex RouteConstraint();

    // Only /api routes: /odata and /graphql are served by their own providers, not by controllers.
    [GeneratedRegex(@"(?<verb>GET|POST|PUT|PATCH|DELETE)\s+(?<path>/api/[A-Za-z0-9{}/_-]*)")]
    private static partial Regex DocumentedRoute();
}
