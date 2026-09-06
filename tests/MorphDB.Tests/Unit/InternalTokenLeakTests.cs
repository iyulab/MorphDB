using System.Text.RegularExpressions;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// This repository is published. Everything committed to it — source, tests, documentation, SDK
/// samples, release notes — is read by people outside the organisation that develops it, and text
/// written for an internal reader does not become internal again by sitting in a comment.
/// <para>
/// The tokens below are the ones a machine can judge without context: identifiers that only mean
/// something inside a private tracker or planning document, and absolute paths off a developer's
/// machine. They leak the same way every time — a fix is written while its ticket is on screen, and
/// the ticket's name goes into the comment explaining the fix. That is invisible to every review
/// that reads the change for correctness, which is why it is checked here instead.
/// </para>
/// <para>
/// Deliberately narrow. A person's name, an internal host name, a project code word — those need
/// context to judge and would produce false positives, and a gate that cries wolf is turned off.
/// What is left is mechanical: if this fails, the text names something no reader outside can look
/// up, and it should say what it means instead.
/// </para>
/// </summary>
public partial class InternalTokenLeakTests
{
    private static readonly string[] SearchedExtensions =
        [".cs", ".md", ".ts", ".py", ".csproj", ".props", ".yml", ".yaml"];

    private static readonly string[] SkippedDirectories =
        [".git", "bin", "obj", "node_modules", "dist", ".venv", "TestResults", "coverage", "claudedocs"];

    public static TheoryData<string, string> Patterns() => new()
    {
        // A tracker ticket or a planning-document item: HD-12, P2-o, BD-20260905-06.
        { "internal ticket or backlog id", @"\b(HD-\d+|P\d+-[a-z]\b|BD-\d{8}-\d+)" },

        // A development cycle number — an artefact of how this project schedules work, not
        // something a reader of the published tree can resolve.
        { "internal cycle number", @"\bcycle-\d+\b" },

        // The private working directory, and the issue-draft file names that live in it.
        { "internal working document", @"\bclaudedocs\b|\bISSUE-[A-Za-z0-9][A-Za-z0-9-]*\.md\b" },

        // A path that only exists on the machine it was written on.
        { "absolute local path", @"(?<![A-Za-z0-9])[A-Za-z]:\\|/home/[a-z]|/Users/[A-Za-z]" },
    };

    [Theory]
    [MemberData(nameof(Patterns))]
    public void No_published_text_names_something_only_an_insider_can_look_up(string what, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        var corpus = Corpus.Value;
        var leaks = new List<string>();

        // A scanner that resolved the wrong root would find nothing and pass forever, which is the
        // one way this gate could be worse than not existing. Hold it to actually reaching the tree.
        corpus.Should().HaveCountGreaterThan(100, "the scan must cover the repository, not an empty directory");
        corpus.Should().Contain(f => f.Path.EndsWith(Path.Combine("docs", "API.md"), StringComparison.Ordinal));

        foreach (var (path, lines) in corpus)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var hit = regex.Match(lines[i]);
                if (hit.Success)
                {
                    leaks.Add($"{path}:{i + 1}: {hit.Value.Trim()}");
                }
            }
        }

        string.Join(Environment.NewLine, leaks).Should().BeEmpty(
            "a published file must not carry {0} — say what the thing means instead of naming a "
            + "record only the authors can open", what);
    }

    /// <summary>
    /// The published tree, read once for the whole class. Each pattern is a separate case, and a
    /// gate that re-walked and re-read the repository per pattern would cost its runtime several
    /// times over for no additional coverage.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<(string Path, string[] Lines)>> Corpus = new(() =>
    {
        var root = RepoRoot();

        return PublishedFiles(root)
            // This file names every pattern it looks for, so it would always match itself.
            .Where(f => !string.Equals(
                Path.GetFileName(f), "InternalTokenLeakTests.cs", StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(root, f), File.ReadAllLines(f)))
            .ToList();
    });

    private static string RepoRoot() => Path.GetDirectoryName(ConstraintBoundaryDoc.RepoFilePath("README.md"))!;

    /// <summary>
    /// Walks the tree pruning as it goes, rather than enumerating everything and filtering after.
    /// Build output alone is tens of thousands of files, and this runs in the unit suite, whose
    /// value is that it answers in seconds.
    /// </summary>
    private static IEnumerable<string> PublishedFiles(string root)
    {
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (SearchedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }
}
