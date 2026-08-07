using System.Text.RegularExpressions;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Reads the "Physical vs Virtual" constraint tables out of the documentation. Shared by the two
/// halves of the constraint-boundary gate — the unit half compares one document's table to
/// another's, the integration half compares a table to what the database actually emitted — so the
/// parsing lives in one place and neither half owns a hand-written copy of the boundary.
/// </summary>
public static partial class ConstraintBoundaryDoc
{
    /// <summary>
    /// Parses a "| name | physical | virtual | rationale |" table into a
    /// name -> "physical" | "virtual" map. Constraint names carry a parenthesised abbreviation in
    /// one document and not the other, so they are normalised to upper case without it. The
    /// rationale column is ignored: each document argues in its own voice, and its own language.
    /// </summary>
    public static Dictionary<string, string> Boundary(string markdown) =>
        BoundaryTableRow().Matches(markdown)
            .Select(m => (
                Name: NormalizeName(m.Groups["name"].Value),
                Physical: m.Groups["physical"].Value.Contains('✅', StringComparison.Ordinal),
                Virtual: m.Groups["virtual"].Value.Contains('✅', StringComparison.Ordinal)))
            .Where(r => r.Physical ^ r.Virtual)
            .ToDictionary(r => r.Name, r => r.Physical ? "physical" : "virtual", StringComparer.Ordinal);

    /// <summary>
    /// The constraint names a boundary table claims the database enforces.
    /// </summary>
    public static HashSet<string> PhysicalNames(string markdown) =>
        Boundary(markdown).Where(e => e.Value == "physical")
            .Select(e => e.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Reads a repository file by its path relative to the repository root, walking up from the
    /// test assembly's location so the tests do not depend on the working directory.
    /// </summary>
    public static string ReadRepoFile(string relativePath) => File.ReadAllText(RepoFilePath(relativePath));

    /// <summary>
    /// Resolves a repository file to its absolute path the same way. A gate whose subject is a
    /// <em>set</em> of files — every document under a directory, rather than one named document —
    /// needs the location rather than the contents, and asking it for the contents of a file it
    /// already knows only to then find its siblings is how a hand-kept list creeps back in.
    /// </summary>
    public static string RepoFilePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{relativePath} not found above {AppContext.BaseDirectory}");
    }

    private static string NormalizeName(string raw) =>
        ParentheticalSuffix().Replace(raw, string.Empty).Trim().ToUpperInvariant();

    [GeneratedRegex(@"^\|\s*(?<name>[A-Za-z][A-Za-z ()]*?)\s*\|(?<physical>[^|]*)\|(?<virtual>[^|]*)\|",
        RegexOptions.Multiline)]
    private static partial Regex BoundaryTableRow();

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex ParentheticalSuffix();
}
