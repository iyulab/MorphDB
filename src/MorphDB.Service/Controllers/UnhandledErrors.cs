using MorphDB.Core.Exceptions;

namespace MorphDB.Service.Controllers;

/// <summary>
/// Per-item error text for batch surfaces that report failures inside a 200 body: expected failure
/// types keep their message (it is ours and it is actionable); anything else is logged and replaced,
/// because internal exception text (driver messages, physical names) is not contract.
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
        if (ex is ValidationException or NotFoundException or System.Collections.Generic.KeyNotFoundException or ArgumentException)
        {
            return ex.Message;
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
