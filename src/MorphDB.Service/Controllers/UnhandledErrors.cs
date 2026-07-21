using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Controllers;

/// <summary>
/// The last catch of an action: maps what the service layer legitimately throws to the status the
/// endpoint documents, and refuses to let anything else masquerade as a client fault.
/// <para>
/// Several controllers used to end in <c>catch (Exception ex) { return BadRequest(ex.Message); }</c>.
/// That branch made every defect a 400 — a caller cannot tell a bug from their own bad request, and
/// retrying "their" error retries our bug — and it copied internal exception text (driver messages,
/// physical names) onto the wire. Unexpected exceptions are now a logged 500 with a fixed message,
/// which is what the data endpoints already did.
/// </para>
/// </summary>
internal static class UnhandledErrors
{
    /// <summary>
    /// Terminal mapping for an action's final <c>catch (Exception)</c>. Typed catches an action
    /// already declares stay in front of this; this covers what they did not.
    /// </summary>
    public static IActionResult Map(ControllerBase controller, ILogger logger, Exception ex, string operation)
    {
        // A canceled request has no useful response — and reporting it as anyone's fault is wrong.
        // Rethrow and let the host observe the cancellation.
        if (ex is OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(ex).Throw();
        }

        return ex switch
        {
            ValidationException v => controller.BadRequest(new ErrorResponse
            {
                Error = "ValidationError",
                Message = v.Message,
                Code = v.ErrorCode
            }),
            NotFoundException n => controller.NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = n.Message,
                Code = n.ErrorCode
            }),
            System.Collections.Generic.KeyNotFoundException k => controller.NotFound(new ErrorResponse
            {
                Error = "NotFound",
                Message = k.Message,
                Code = "TABLE_NOT_FOUND"
            }),
            ArgumentException a => controller.BadRequest(new ErrorResponse
            {
                Error = "BadRequest",
                Message = a.Message,
                Code = "INVALID_ARGUMENT"
            }),
            _ => Unexpected(controller, logger, ex, operation)
        };
    }

    /// <summary>
    /// Per-item variant for batch surfaces that report failures inside a 200 body: expected failure
    /// types keep their message (it is ours and it is actionable); anything else is logged and
    /// replaced, for the same reason the top-level branch does not echo exception text.
    /// </summary>
    public static string ItemMessage(ILogger logger, Exception ex, string operation)
    {
        if (ex is ValidationException or NotFoundException or System.Collections.Generic.KeyNotFoundException or ArgumentException)
        {
            return ex.Message;
        }

        UnhandledErrorsLogs.UnexpectedError(logger, ex, operation);
        return "An unexpected error occurred";
    }

    private static ObjectResult Unexpected(ControllerBase controller, ILogger logger, Exception ex, string operation)
    {
        UnhandledErrorsLogs.UnexpectedError(logger, ex, operation);
        return controller.StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
        {
            Error = "InternalError",
            Message = "An unexpected error occurred",
            Code = "INTERNAL_ERROR"
        });
    }
}

internal static partial class UnhandledErrorsLogs
{
    [LoggerMessage(LogLevel.Error, "Unexpected error during {Operation}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string operation);
}
