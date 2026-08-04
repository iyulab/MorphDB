using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Unit;

/// <summary>
/// The doc-server parity gate for error codes (issue unknown-column-code-mismatch — the third
/// recurrence of a ghost contract living in the docs). docs/API.md tells consumers to branch on
/// <c>code</c>; this test holds the Errors table to the set of codes the server can actually
/// answer, so a code added on either side without the other trips the suite. The expected set
/// below is the audited inventory of every envelope-code production site: the typed exceptions
/// (<c>MorphDbException</c> subtypes + <c>GlobalExceptionHandler</c> mappings), the write-failure
/// funnel (<c>WriteFailure.CodeFor</c>), and the controllers' inline envelopes.
/// <para>
/// This half compares two written lists and can only ever say they agree. Whether a response
/// carries a code at all — and whether the code it carries is one of these — is the other half's
/// question, in <c>ErrorEnvelopeCodeContractTests</c>. An inventory kept by hand drifts, and a
/// gate that only reads inventories drifts with it.
/// </para>
/// </summary>
public class DocsErrorCodeParityTests
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
        // SchemaException / ValidationException codes the schema routes pass through unchanged —
        // the controllers answer with ex.ErrorCode rather than flattening it, so these reach the
        // wire whether the handler or an action's catch answers
        "INVALID_NAME", "RESERVED_NAME", "SYSTEM_COLUMN", "UNSAFE_TYPE_CAST", "INVALID_OPERATION",
        "DDL_EXECUTION_FAILED", "BATCH_DDL_FAILED", "INDEX_NOT_FOUND", "RELATION_NOT_FOUND",
        // Write pipeline funnel (WriteFailure.CodeFor)
        "UNKNOWN_COLUMN",
        // Controllers' inline envelopes
        "INVALID_FILTER", "RECORD_NOT_FOUND", "ROW_STATE_NOT_ENABLED",
        "EMPTY_BATCH", "EMPTY_DATA", "EMPTY_TRANSACTION", "EMPTY_RECORD_IDS",
        "MISSING_KEY_COLUMNS", "FILTER_REQUIRED", "AGGREGATION_REQUIRED",
        "JOB_NOT_FOUND", "JOB_NOT_COMPLETED", "AUDIT_LOG_NOT_FOUND",
        "VIEW_NOT_FOUND", "NOT_MATERIALIZED", "WEBHOOK_NOT_FOUND",
    };

    [Fact]
    public void The_docs_errors_table_matches_the_codes_the_server_answers()
    {
        var documented = DocsErrorCodes.Documented();

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
        DocsErrorCodes.Documented().Should().NotContain("VALIDATION_FAILED");
        ServerCodes.Should().NotContain("VALIDATION_FAILED");

        // VIEW_EXISTS was a catch filter guarding a code nothing threw — the view manager raises
        // DuplicateNameException, so the clause was unreachable and the code was never on the wire.
        DocsErrorCodes.Documented().Should().NotContain("VIEW_EXISTS");
        ServerCodes.Should().NotContain("VIEW_EXISTS");
    }
}
