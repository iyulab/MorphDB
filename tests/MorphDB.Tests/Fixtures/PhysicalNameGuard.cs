using System.Text.RegularExpressions;

namespace MorphDB.Tests.Fixtures;

/// <summary>
/// The physical-name-leak contract, promoted out of <c>PhilosophyComplianceTests</c>'s private
/// copy once REST, GraphQL, export and view contract tests needed the same predicate against real
/// server responses, not just hand-built model objects. Physical names are hash-based
/// (<c>tbl_</c>/<c>col_</c>/<c>idx_</c>/<c>fk_</c>/<c>pk_</c>/<c>uq_</c>/<c>chk_</c>/<c>view_</c> +
/// an 8+ char hex suffix) — a consumer-facing surface must never carry one.
/// </summary>
public static partial class PhysicalNameGuard
{
    private static readonly string[] Prefixes =
        ["tbl_", "col_", "idx_", "fk_", "pk_", "uq_", "chk_", "view_"];

    /// <summary>Whether a single candidate name matches the physical-name pattern.</summary>
    public static bool IsPhysicalName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var prefix in Prefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = name[prefix.Length..];
                return suffix.Length >= 8 && suffix.All(c =>
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F'));
            }
        }

        return false;
    }

    /// <summary>
    /// Scans a raw response body (JSON, CSV, ...) for physical-name-shaped substrings, so a
    /// contract test can pin a whole payload at once instead of enumerating candidate keys by
    /// hand. Returns the distinct matches found — empty when the surface is clean.
    /// </summary>
    public static IReadOnlyList<string> FindPhysicalNames(string text) =>
        [.. PhysicalNamePattern().Matches(text).Select(m => m.Value).Distinct()];

    [GeneratedRegex(@"\b(?:tbl|col|idx|fk|pk|uq|chk|view)_[0-9a-fA-F]{8,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalNamePattern();
}
