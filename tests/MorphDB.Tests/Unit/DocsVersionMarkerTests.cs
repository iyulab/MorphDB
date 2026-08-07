using System.Text.RegularExpressions;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// Holds the documentation's statement of which version it describes.
/// <para>
/// The parity gates next door hold the documentation to the server — but to the server on
/// <c>main</c>, while a reader is running the published image, and the repository's front page is
/// <c>main</c> by default. So the documentation can be exactly right and still describe a server
/// nobody can run: it did, promising a project-creation recipe the published version answers with
/// a 400. That gap is not a drift between two files, so no parity gate can see it; what closes it
/// is saying which version each statement belongs to.
/// </para>
/// <para>
/// The convention is a <c>Since x.y.z</c> marker on anything the published image does not serve
/// yet, and it is one-directional by nature: nothing can find a behaviour someone forgot to mark.
/// What is held here is the half that can be — a marker names a version the published one has not
/// reached, and the version the documentation calls published is the one the README actually pins.
/// The published version is read from that pin rather than named here, so a release moves all
/// three together or fails.
/// </para>
/// </summary>
public partial class DocsVersionMarkerTests
{
    [Fact]
    public void The_published_version_is_pinned_once_and_the_same_everywhere()
        => PublishedVersion().Should().NotBeNull();

    [Fact]
    public void No_marker_names_a_version_the_published_one_has_already_reached()
    {
        var published = PublishedVersion()!;

        var stale = DocFiles()
            .SelectMany(file => SinceMarker().Matches(File.ReadAllText(file))
                .Select(m => (File: Path.GetFileName(file), Version: Version.Parse(m.Groups["version"].Value))))
            .Where(m => m.Version <= published)
            .Select(m => $"{m.File}: Since {m.Version}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, stale).Should().BeEmpty(
            $"the published image is {published}, so a marker at or below it describes behaviour a "
            + "reader can already run — the marker has done its work and belongs in the release that "
            + "shipped it, not in the document afterwards");
    }

    [Fact]
    public void The_version_the_reference_calls_published_is_the_one_the_README_pins()
    {
        var reference = ConstraintBoundaryDoc.ReadRepoFile("docs/API.md");

        PublishedClaim().Match(reference).Groups["version"].Value
            .Should().Be(PublishedVersion()!.ToString(),
                "a reader takes that sentence as the answer to \"which server is this\", and it is "
                + "the one statement in the document no parity gate can check against the server");

        TagPointer().Match(reference).Groups["version"].Value
            .Should().Be(PublishedVersion()!.ToString(),
                "the tag it sends a reader to for released documentation has to be the released one");
    }

    /// <summary>
    /// The published version, read from the image the README tells a reader to pull. Every pin in
    /// the README must agree — two of them saying different things is the same defect one level up.
    /// </summary>
    private static Version? PublishedVersion()
    {
        var pins = ImagePin().Matches(ConstraintBoundaryDoc.ReadRepoFile("README.md"))
            .Select(m => m.Groups["version"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        pins.Should().ContainSingle("the README must pin one published image version, not several");

        return Version.Parse(pins[0]);
    }

    private static IEnumerable<string> DocFiles()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(ConstraintBoundaryDoc.RepoFilePath("docs/API.md"))!);
        return directory.GetFiles("*.md").Select(f => f.FullName);
    }

    [GeneratedRegex(@"ghcr\.io/iyulab/morphdb:(?<version>\d+\.\d+\.\d+)")]
    private static partial Regex ImagePin();

    [GeneratedRegex(@"\*\*Since (?<version>\d+\.\d+\.\d+)")]
    private static partial Regex SinceMarker();

    [GeneratedRegex(@"published version, \*\*(?<version>\d+\.\d+\.\d+)\*\*")]
    private static partial Regex PublishedClaim();

    // The sentence wraps inside a blockquote, so the gap between "tag:" and the reference carries
    // the quote marker as well as whitespace.
    [GeneratedRegex(@"at its tag:[\s>]*`docs/API\.md` at `v(?<version>\d+\.\d+\.\d+)`")]
    private static partial Regex TagPointer();
}
