using MorphDB.Service.ErrorHandling;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Per-item error text for batch surfaces that report failures inside a 200 body: a failure that is the
/// caller's (the classification every door reads — <see cref="ErrorClassification"/>) keeps its message,
/// which is ours and actionable; anything else is logged and replaced, because internal exception text
/// (driver messages, physical names) is not contract. Keeping a list of the expected types here left out
/// the ones added later — a row-level-security refusal was reported as an unexpected error.
/// <para>
/// The request-level counterpart lives in <see cref="ErrorHandling.GlobalExceptionHandler"/> — an
/// exception that escapes an action entirely is mapped there. This helper exists for the surfaces
/// where an item's failure must not fail the request.
/// </para>
/// </summary>
internal static class UnhandledErrors
{
    public static string ItemMessage(ILogger logger, Exception ex, string operation)
    {
        var (status, envelope) = ErrorClassification.Envelope(ex);
        if (status < StatusCodes.Status500InternalServerError)
        {
            return envelope.Message ?? ex.Message;
        }

        UnhandledErrorsLogs.UnexpectedError(logger, ex, operation);
        return "An unexpected error occurred";
    }
}

internal static partial class UnhandledErrorsLogs
{
    [LoggerMessage(LogLevel.Error, "Unexpected error during {Operation}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string operation);
}
