using MorphDB.Core.Models;

namespace MorphDB.Service.ErrorHandling;

/// <summary>
/// Maps a failed write-pipeline result onto the error envelope's <c>code</c>. <c>UNKNOWN_COLUMN</c>
/// keeps its documented, branchable identity — it has a dedicated remedy (fix the field name, or
/// opt in with <c>?ignoreUnknown=true</c>). Every other cause, and any mix of causes, answers the
/// generic <c>VALIDATION_ERROR</c>: the same code physical constraint violations translate to, so
/// one cause never answers two different codes depending on which layer caught it. The previous
/// <c>VALIDATION_FAILED</c> appeared in no documentation and is retired.
/// </summary>
internal static class WriteFailure
{
    /// <summary>
    /// Whether the write found no row to write to. A route that updates a record reads it first and
    /// answers "not found" itself when the read finds nothing — but the write can still find no
    /// row: one deleted in between, or one its caller may read and a row-level security policy for
    /// updates does not admit. That is the same answer, not a validation failure.
    /// </summary>
    public static bool IsNotFound(WriteResult result) =>
        result.Errors.Any(e => e.Code == ValidationErrorCodes.NotFound);

    public static string CodeFor(WriteResult result) =>
        result.Errors.Count > 0 && result.Errors.All(e => e.Code == ValidationErrorCodes.UnknownColumn)
            ? ValidationErrorCodes.UnknownColumn
            : "VALIDATION_ERROR";
}
