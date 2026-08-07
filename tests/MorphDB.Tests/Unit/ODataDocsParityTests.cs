using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Service.OData;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for the two things an OData route's existence does not settle:
/// which query options the service reads, and how a key is written in the URL.
/// <para>
/// <see cref="DocsRouteParityTests"/> answers "does a route exist", and for OData that leaves the
/// interesting half open. A query option is not part of any route template, so a documented one
/// can be accepted, ignored, and answered <c>200</c> — which is what happened: <c>$expand</c> was
/// listed as supported, bound by both read actions, and read by nothing. A key literal is inside
/// a route segment, so the route matcher treats it as a wildcard and never sees that the
/// documented form was quoted like a string while the model types the key as a GUID.
/// </para>
/// <para>
/// Both sides are read from the controller rather than listed here. Options come from the
/// <see cref="FromQueryAttribute"/> names its actions bind, and the key form comes from the CLR
/// type its actions bind the key as — so deleting a dead binding is enough to make the
/// documentation that still advertises it fail.
/// </para>
/// </summary>
public partial class ODataDocsParityTests
{
    [Fact]
    public void Every_query_option_the_documentation_lists_is_one_an_action_binds()
    {
        var bound = typeof(MorphODataController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.GetCustomAttribute<FromQueryAttribute>()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        var documented = DocumentedOptions();

        documented.Should().NotBeEmpty("API.md must list the OData query options for this gate to mean anything");

        string.Join(", ", documented.Where(o => !bound.Contains(o)).OrderBy(o => o, StringComparer.Ordinal))
            .Should().BeEmpty(
                "an option listed as supported that no action binds is answered with a 200 that "
                + "quietly did less than the caller asked for — the worst shape a ghost contract "
                + "takes, because nothing tells the caller it was ignored");
    }

    [Fact]
    public void Every_documented_key_is_written_the_way_the_action_binds_it()
    {
        // The route template that carries a key, and the CLR type its key parameter binds as.
        // Reading the type rather than naming a syntax is what keeps this true if the key ever
        // stops being a GUID.
        var keyType = typeof(MorphODataController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetParameters())
            .Where(p => p.Name == "key")
            .Select(p => p.ParameterType)
            .Distinct()
            .Should().ContainSingle("every keyed OData action binds the same key type").Subject;

        keyType.Should().Be<Guid>();

        var documented = DocumentedKeys();

        // Without this the check passes on a document that stopped showing keyed routes at all --
        // green because there was nothing to look at, which reads exactly like green because
        // everything was right.
        documented.Should().NotBeEmpty("API.md must show how a keyed OData route is addressed");

        var wrong = documented
            .Where(key => !Guid.TryParse(key, out _))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        string.Join(", ", wrong).Should().BeEmpty(
            "the metadata document types the key as Edm.Guid, and OData v4 writes a GUID key bare. "
            + "A quoted literal is a string key's syntax; the server answers it with a binding "
            + "failure, so a reader who copies the documented form cannot address a row at all");
    }

    private static string DocsText() => ConstraintBoundaryDoc.ReadRepoFile("docs/API.md");

    /// <summary>
    /// The <c>$options</c> named in the OData section's supported-options line. Taken from that
    /// line rather than from every <c>$</c> in the document, because the same characters appear in
    /// example URLs and in <c>$metadata</c>, and the claim being gated is the list.
    /// </summary>
    private static IReadOnlyList<string> DocumentedOptions()
        => SupportedOptionsLine().Matches(DocsText())
            .SelectMany(m => OptionToken().Matches(m.Groups["options"].Value))
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>The key literals written inside <c>/odata/…(key)</c> in the documentation.</summary>
    private static IReadOnlyList<string> DocumentedKeys()
        => DocumentedKey().Matches(DocsText())
            .Select(m => m.Groups["key"].Value)
            .ToList();

    [GeneratedRegex(@"\*\*Supported query options\*\*:(?<options>[^\r\n]*)")]
    private static partial Regex SupportedOptionsLine();

    [GeneratedRegex(@"\$[a-z]+")]
    private static partial Regex OptionToken();

    [GeneratedRegex(@"/odata/[A-Za-z0-9_]+\((?<key>[^)]*)\)")]
    private static partial Regex DocumentedKey();
}
