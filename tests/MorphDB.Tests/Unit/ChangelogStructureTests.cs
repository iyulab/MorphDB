using System.Text.RegularExpressions;

using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the shape of the release notes a consumer reads at upgrade time.
/// <para>
/// A changelog block is a set of buckets — one per kind of change — and the reader's use for it is
/// to find the kind that concerns them. That breaks quietly: several changes land separately, each
/// adds the heading it needs without looking at what is already there, and the block ends up
/// splitting one kind across two places. Nothing else notices. The compiler has no opinion about a
/// markdown heading, and the parity gates in this folder all read documents against the code — the
/// changelog is the one consumer-facing surface with no counterpart to be compared to. This
/// repository's own released blocks show what that costs: one of them carries three separate
/// <c>Changed</c> sections.
/// </para>
/// <para>
/// Two rules hold that shape, and both are confined to the <c>Unreleased</c> block. Released blocks
/// are the record of what shipped; correcting them afterwards edits history, which this repository
/// has already declined to do elsewhere. The cost of the confinement is that the duplicates already
/// in those blocks stay there.
/// </para>
/// <para>
/// The allowed vocabulary is mostly not a list written here. It is read from this repository's own
/// released blocks, over a small conventional floor — so the file ports to a sibling repository
/// unchanged, and a repository that says <c>Docs</c> where this one says <c>Documentation</c> is
/// held to its own word rather than to this one's.
/// </para>
/// </summary>
public partial class ChangelogStructureTests
{
    /// <summary>The block these rules read; released blocks are history and are left alone.</summary>
    private const string Unreleased = "Unreleased";

    /// <summary>
    /// Section names allowed whether or not a released block has carried one yet — reaching for the
    /// standard word is never the mistake this gate is looking for.
    /// <para>
    /// The first six are Keep a Changelog's. <c>Breaking</c> is the seventh because the standard has
    /// no section for a broken contract and these packages give it one. It is named here rather than
    /// left to history because a word's first use would otherwise always fail — which is what the
    /// sibling repository hit, where the released blocks had never carried it. Adding a word here is
    /// a deliberate act, which is the point: an unrecognised heading should cost a decision.
    /// </para>
    /// </summary>
    private static readonly string[] Conventional =
        ["Added", "Changed", "Deprecated", "Removed", "Fixed", "Security", "Breaking"];

    [Fact]
    public void The_unreleased_block_names_each_kind_of_change_once()
    {
        var repeated = Repeated(Headings(Changelog(), Unreleased));

        string.Join(", ", repeated).Should().BeEmpty(
            "a reader looks under one heading for the changes of that kind, and a second heading of "
            + "the same name puts the rest of them somewhere they will not look — which is what "
            + "happens when separate changes each add the heading they need without reading the block");
    }

    [Fact]
    public void Every_unreleased_heading_is_a_bare_word_this_repository_already_uses()
    {
        var changelog = Changelog();
        var vocabulary = Vocabulary(changelog);

        var unknown = Headings(changelog, Unreleased)
            .Where(h => !vocabulary.Contains(h))
            .ToList();

        string.Join(", ", unknown).Should().BeEmpty(
            $"the sections this repository uses are [{string.Join(", ", vocabulary.OrderBy(v => v, StringComparer.Ordinal))}], "
            + "and a heading outside them is either a new kind of change nobody decided to introduce "
            + "or the same kind under a second name. A heading carrying prose after the word is the "
            + "second case: it reads as its own section, so the kind gets split — which is how one "
            + "released block in this file came to hold three 'Changed' sections");
    }

    /// <summary>
    /// A CHANGELOG bullet that claims one thing is equivalent to or a substitute for another — a
    /// removed member "works the same as" an existing one, a migration path "produces the same
    /// result" — is a claim prose alone cannot be held to; the export-filter removal in this same
    /// file once claimed an equivalence the running server did not have. This rule does not try to
    /// detect such a claim in the prose (that was tried and rejected — see the CHANGELOG-migration
    /// issue this rule answers): it requires that wherever an author already marked one with a
    /// <c>Verified-by:</c> tag, the test named actually exists. It checks existence only, not that
    /// the test proves what the bullet claims — a semantic check would need to understand prose the
    /// same unbounded way the rejected alternative did.
    /// </summary>
    [Fact]
    public void Every_Verified_by_tag_in_the_unreleased_block_names_a_test_that_exists()
    {
        var tags = VerifiedByTags(UnreleasedText(Changelog()));
        var testSource = TestFileContents();

        var unresolved = tags.Where(tag => !TestExists(tag, testSource)).ToList();

        string.Join(", ", unresolved).Should().BeEmpty(
            "a Verified-by tag is where a claim of equivalence or substitutability points at the "
            + "test that backs it — a tag naming a test that does not exist reads as verified when "
            + "nothing was checked, which is worse than carrying no tag at all");
    }

    [Fact]
    public void The_gate_reports_a_Verified_by_tag_naming_no_real_test()
    {
        const string broken = "- Some claim. (Verified-by: NoSuchClass.NoSuchMethod)";

        var tags = VerifiedByTags(broken);

        tags.Should().ContainSingle().Which.Should().Be("NoSuchClass.NoSuchMethod");
        TestExists(tags[0], TestFileContents()).Should().BeFalse(
            "without this the existence check above would pass over any tag and be vacuous");
    }

    [Fact]
    public void The_block_the_two_rules_read_is_named_the_one_way_they_look_for()
    {
        var misnamed = Blocks(Changelog())
            .Select(b => b.Name)
            .Where(n => n.Trim('[', ']').Equals(Unreleased, StringComparison.OrdinalIgnoreCase))
            .Where(n => n != Unreleased)
            .ToList();

        string.Join(", ", misnamed).Should().BeEmpty(
            $"the two rules above read the block named exactly '{Unreleased}', and a block that means "
            + "the same thing under another spelling — the bracketed form is the common one — leaves "
            + "them reading nothing at all. They would still pass, which is the failure this catches: "
            + "a renamed block turns both gates vacuous rather than red");
    }

    [Fact]
    public void The_gate_reports_a_block_that_breaks_both_rules()
    {
        const string broken = """
            # Changelog

            ## Unreleased

            ### Added

            - one

            ### Added

            - two

            ### Added — the part that reads as its own section

            - three

            ### Refactored

            - four

            ## 1.0.0

            ### Added

            - shipped
            """;

        var headings = Headings(broken, Unreleased);

        Repeated(headings).Should().Contain("Added",
            "without this the uniqueness gate above passes over any block and is vacuous");

        headings.Where(h => !Vocabulary(broken).Contains(h)).Should()
            .Contain("Added — the part that reads as its own section").And
            .Contain("Refactored",
                "the vocabulary gate has to reject both a word nobody introduced and a canonical "
                + "word carrying prose — otherwise it passes over any block and is vacuous");
    }

    private static string Changelog() => ConstraintBoundaryDoc.ReadRepoFile("CHANGELOG.md");

    /// <summary>
    /// The <c>###</c> headings of one <c>##</c> block, verbatim — prose after the word included, so
    /// the vocabulary check is what rejects it rather than the reader silently dropping it.
    /// </summary>
    private static IReadOnlyList<string> Headings(string changelog, string block) =>
        [.. Blocks(changelog).Where(b => b.Name == block).SelectMany(b => b.Headings)];

    /// <summary>
    /// The canonical sections plus every bare single-word heading the released blocks use. Read from
    /// the document rather than listed here so the rule travels with the repository it is applied to.
    /// </summary>
    private static IReadOnlySet<string> Vocabulary(string changelog)
    {
        var released = Blocks(changelog)
            .Where(b => b.Name != Unreleased)
            .SelectMany(b => b.Headings)
            .Where(h => Regex.IsMatch(h, @"^[A-Za-z]+$"));

        return new HashSet<string>([.. Conventional, .. released], StringComparer.Ordinal);
    }

    /// <summary>
    /// The document as its <c>##</c> blocks, each with the <c>###</c> headings under it. One walk
    /// serves both rules and the vocabulary, so they cannot disagree about where a block ends.
    /// </summary>
    private static IEnumerable<(string Name, IReadOnlyList<string> Headings)> Blocks(string changelog)
    {
        string? name = null;
        List<string> headings = [];

        foreach (var line in changelog.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (name is not null)
                {
                    yield return (name, headings);
                }

                name = line[3..].Trim();
                headings = [];
            }
            else if (name is not null && line.StartsWith("### ", StringComparison.Ordinal))
            {
                headings.Add(line[4..].Trim());
            }
        }

        if (name is not null)
        {
            yield return (name, headings);
        }
    }

    private static IReadOnlyList<string> Repeated(IReadOnlyList<string> headings) =>
        [.. headings.GroupBy(h => h, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(k => k, StringComparer.Ordinal)];

    /// <summary>
    /// The raw text of the <c>##</c> block named <see cref="Unreleased"/> — headings included, unlike
    /// <see cref="Headings"/>, because a <c>Verified-by:</c> tag lives in bullet prose rather than a
    /// heading. Confined to <c>Unreleased</c> for the same reason the other two rules are: a released
    /// block is history, and a test renamed or removed later must not turn an already-shipped claim
    /// red.
    /// </summary>
    private static string UnreleasedText(string changelog)
    {
        var lines = new List<string>();
        var inBlock = false;

        foreach (var line in changelog.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inBlock = line[3..].Trim() == Unreleased;
                continue;
            }

            if (inBlock)
            {
                lines.Add(line);
            }
        }

        return string.Join('\n', lines);
    }

    private static IReadOnlyList<string> VerifiedByTags(string text) =>
        [.. VerifiedByTag().Matches(text).Select(m => m.Groups["test"].Value)];

    /// <summary>
    /// Whether a test named <c>ClassName.MethodName</c> exists anywhere under the test project — a
    /// file declaring that class and, in the same file, a method of that name. Existence only: this
    /// does not run the test or read what it asserts, matching the gate's declared scope.
    /// </summary>
    private static bool TestExists(string tag, IReadOnlyList<string> testFileContents)
    {
        var parts = tag.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var classPattern = new Regex($@"\bclass\s+{Regex.Escape(parts[0])}\b");
        var methodPattern = new Regex($@"\b{Regex.Escape(parts[1])}\s*\(");

        return testFileContents.Any(text => classPattern.IsMatch(text) && methodPattern.IsMatch(text));
    }

    private static IReadOnlyList<string> TestFileContents()
    {
        var testsRoot = Path.GetDirectoryName(
            ConstraintBoundaryDoc.RepoFilePath("tests/MorphDB.Tests/MorphDB.Tests.csproj"))!;

        return [.. new DirectoryInfo(testsRoot)
            .GetFiles("*.cs", SearchOption.AllDirectories)
            .Select(f => File.ReadAllText(f.FullName))];
    }

    [GeneratedRegex(@"Verified-by:\s*(?<test>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex VerifiedByTag();
}
