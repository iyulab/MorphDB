using System.Text.RegularExpressions;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// Reads the error codes out of the <c>## Errors</c> table in <c>docs/API.md</c>. Shared by the two
/// halves of the error-code gate — the unit half compares the table to the inventory of production
/// sites, the integration half compares it to the codes real responses actually carry — so the
/// parsing lives in one place and neither half keeps its own copy of the documented set.
/// </summary>
public static partial class DocsErrorCodes
{
    public static HashSet<string> Documented()
    {
        var apiMd = File.ReadAllText(FindDocsFile("API.md"));
        var errorsSection = apiMd[apiMd.IndexOf("## Errors", StringComparison.Ordinal)..];

        return ErrorTableRow().Matches(errorsSection)
            .Select(m => m.Groups["code"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

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

    [GeneratedRegex(@"^\|\s*\d{3}\s*\|\s*`(?<code>[A-Z_]+)`\s*\|", RegexOptions.Multiline)]
    private static partial Regex ErrorTableRow();
}
