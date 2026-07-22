using Microsoft.AspNetCore.Diagnostics;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.ErrorHandling;

/// <summary>
/// The single authority for what an escaped exception becomes on the wire.
/// <para>
/// Before this handler existed the service had no global error path at all: whatever an action's
/// catch chain did not name fell through to the framework default — a 500 with an empty body.
/// A caller who sent an unknown column type got exactly that: nothing to read, no code to branch
/// on, and no way to tell their mistake from our defect. Domain exceptions map to the status the
/// API documents; everything unexpected is logged and replaced with a fixed INTERNAL_ERROR
/// envelope, because internal exception text (driver messages, physical names) is not contract.
/// </para>
/// <para>
/// Actions may still declare typed catches in front of this when an endpoint documents a more
/// specific code (e.g. INVALID_FILTER); this handler is the floor, not a competitor.
/// </para>
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // A canceled request has no useful response — and reporting it as anyone's fault is wrong.
        // Decline to handle it and let the host observe the cancellation.
        if (exception is OperationCanceledException)
        {
            return false;
        }

        // Order matters: the most derived domain types come first (ValidationException and the
        // schema exceptions all descend from MorphDbException).
        var (status, error, code) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "ValidationError", v.ErrorCode),
            MissingProjectException m => (StatusCodes.Status400BadRequest, "BadRequest", m.ErrorCode),
            TableNotFoundException t => (StatusCodes.Status404NotFound, "NotFound", t.ErrorCode),
            // A column is not an addressable resource of any route — an unknown column is a defect
            // in the request body/query, so it stays 400 (the contract aggregation documented).
            ColumnNotFoundException c => (StatusCodes.Status400BadRequest, "BadRequest", c.ErrorCode),
            DuplicateNameException d => (StatusCodes.Status409Conflict, "Conflict", d.ErrorCode),
            DuplicateSlugException ds => (StatusCodes.Status409Conflict, "Conflict", ds.ErrorCode),
            ProjectNotFoundException p => (StatusCodes.Status404NotFound, "NotFound", p.ErrorCode),
            SchemaVersionConflictException s => (StatusCodes.Status409Conflict, "Conflict", s.ErrorCode),
            LockAcquisitionException l => (StatusCodes.Status409Conflict, "Conflict", l.ErrorCode),
            NotFoundException n => (StatusCodes.Status404NotFound, "NotFound", n.ErrorCode),
            System.Collections.Generic.KeyNotFoundException => (StatusCodes.Status404NotFound, "NotFound", "TABLE_NOT_FOUND"),
            MorphDB.Core.Exceptions.SchemaException se => (StatusCodes.Status400BadRequest, "SchemaError", se.ErrorCode),
            ArgumentException => (StatusCodes.Status400BadRequest, "BadRequest", "INVALID_ARGUMENT"),
            _ => (StatusCodes.Status500InternalServerError, "InternalError", "INTERNAL_ERROR")
        };

        string? message;
        if (status == StatusCodes.Status500InternalServerError)
        {
            GlobalExceptionHandlerLogs.UnexpectedError(_logger, exception, httpContext.Request.Method, httpContext.Request.Path);
            message = "An unexpected error occurred";
        }
        else
        {
            message = exception.Message;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorResponse { Error = error, Message = message, Code = code },
            cancellationToken);
        return true;
    }
}

internal static partial class GlobalExceptionHandlerLogs
{
    [LoggerMessage(LogLevel.Error, "Unexpected error handling {Method} {Path}")]
    public static partial void UnexpectedError(ILogger logger, Exception exception, string method, string path);
}
