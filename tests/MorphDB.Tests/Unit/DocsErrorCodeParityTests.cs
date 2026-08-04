using System.Text.RegularExpressions;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for error codes (issue unknown-column-code-mismatch — the third
/// recurrence of a ghost contract living in the docs). docs/API.md tells consumers to branch on
/// <c>code</c>; this test holds the Errors table to the set of codes the server can actually
/// answer, so a code added on either side without the other trips the suite. The expected set
/// below is the audited inventory of every envelope-code production site: the typed exceptions
/// (<c>MorphDbException</c> subtypes + <c>GlobalExceptionHandler</c> mappings), the write-failure
/// funnel (<c>WriteFailure.CodeFor</c>), and the controllers' inline envelopes.
/// </summary>
public partial class DocsErrorCodeParityTests
{
    private static readonly IReadOnlySet<string> ServerCodes = new HashSet<string>
    {
        // GlobalExceptionHandler — typed exceptions
        "VALIDATION_ERROR", "MISSING_PROJECT", "TABLE_NOT_FOUND", "COLUMN_NOT_FOUND",
        "DUPLICATE_NAME", "DUPLICATE_SLUG", "PROJECT_NOT_FOUND", "SCHEMA_VERSION_CONFLICT",
        "LOCK_ACQUISITION_FAILED", "NOT_FOUND", "INVALID_ARGUMENT", "INTERNAL_ERROR",
        "UNAUTHENTICATED", "FORBIDDEN",
        // Secret enforcement — the middleware writes its own envelope (it denies before MVC) and
        // the management routes decline when no master secret is injected
        "SECRETS_NOT_CONFIGURED",
        // SchemaException codes with contract weight
        "TABLE_HAS_DEPENDENTS", "INVALID_EXPRESSION",
        // Write pipeline funnel (WriteFailure.CodeFor)
        "UNKNOWN_COLUMN",
        // Controllers' inline envelopes
        "INVALID_FILTER", "RECORD_NOT_FOUND", "ROW_STATE_NOT_ENABLED",
        "EMPTY_BATCH", "EMPTY_DATA", "EMPTY_TRANSACTION", "EMPTY_RECORD_IDS",
        "MISSING_KEY_COLUMNS", "FILTER_REQUIRED", "AGGREGATION_REQUIRED",
        "JOB_NOT_FOUND", "JOB_NOT_COMPLETED", "AUDIT_LOG_NOT_FOUND",
        "VIEW_NOT_FOUND", "NOT_MATERIALIZED",
    };

    [Fact]
    public void The_docs_errors_table_matches_the_codes_the_server_answers()
    {
        var documented = DocumentedCodes();

        documented.Should().NotBeEmpty("the Errors table must exist and carry codes");
        documented.Should().BeEquivalentTo(ServerCodes,
            "docs/API.md says to branch on `code` — a code documented but never answered is a ghost " +
            "contract, and a code answered but not documented is invisible to consumers. Fix whichever " +
            "side drifted (and update this inventory only for a deliberate contract change).");
    }

    [Fact]
    public void Retired_codes_do_not_come_back()
    {
        // VALIDATION_FAILED appeared in no documentation and was retired for VALIDATION_ERROR /
        // UNKNOWN_COLUMN (cycle-72). Nothing may quietly reintroduce it.
        DocumentedCodes().Should().NotContain("VALIDATION_FAILED");
        ServerCodes.Should().NotContain("VALIDATION_FAILED");
    }

    private static HashSet<string> DocumentedCodes()
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
