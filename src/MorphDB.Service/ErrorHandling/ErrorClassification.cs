using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.ErrorHandling;

/// <summary>
/// What an exception that escaped a request means to the caller — one table, read by the REST
/// handler and the GraphQL error filter alike, so a mistake is named the same way on every door.
/// A status of 500 means «ours, not the caller's»: its text is not contract and is never shown.
/// </summary>
internal static class ErrorClassification
{
    /// <summary>
    /// The envelope an escaped exception answers with: its classification, its message (a fixed one
    /// when the fault is ours), and — for a validation failure that names a field — that field's reason
    /// under <c>details</c>.
    /// </summary>
    public static (int Status, ErrorResponse Envelope) Envelope(Exception exception)
    {
        var (status, error, code) = Classify(exception);
        return (status, new ErrorResponse
        {
            Error = error,
            Code = code,
            Message = status >= StatusCodes.Status500InternalServerError ? "An unexpected error occurred" : exception.Message,
            Details = exception is ValidationException { Field: { } field, Reason: { } reason }
                ? new Dictionary<string, string[]> { [field] = [reason] }
                : null,
        });
    }

    public static (int Status, string Error, string Code) Classify(Exception exception)
    {
        // Order matters: the most derived domain types come first (ValidationException and the
        // schema exceptions all descend from MorphDbException).
        return exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, "ValidationError", v.ErrorCode),
            UnauthenticatedException u => (StatusCodes.Status401Unauthorized, "Unauthenticated", u.ErrorCode),
            ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden", f.ErrorCode),
            MissingProjectException m => (StatusCodes.Status400BadRequest, "BadRequest", m.ErrorCode),
            MalformedProjectIdException mpi => (StatusCodes.Status400BadRequest, "BadRequest", mpi.ErrorCode),
            TableNotFoundException t => (StatusCodes.Status404NotFound, "NotFound", t.ErrorCode),
            // A column is not an addressable resource of any route — an unknown column is a defect
            // in the request body/query, so it stays 400 (the contract aggregation documented).
            ColumnNotFoundException c => (StatusCodes.Status400BadRequest, "BadRequest", c.ErrorCode),
            DuplicateNameException d => (StatusCodes.Status409Conflict, "Conflict", d.ErrorCode),
            DuplicateSlugException ds => (StatusCodes.Status409Conflict, "Conflict", ds.ErrorCode),
            DuplicateProjectIdException dp => (StatusCodes.Status409Conflict, "Conflict", dp.ErrorCode),
            ProjectNotFoundException p => (StatusCodes.Status404NotFound, "NotFound", p.ErrorCode),
            SchemaVersionConflictException s => (StatusCodes.Status409Conflict, "Conflict", s.ErrorCode),
            LockAcquisitionException l => (StatusCodes.Status409Conflict, "Conflict", l.ErrorCode),
            NotFoundException n => (StatusCodes.Status404NotFound, "NotFound", n.ErrorCode),
            System.Collections.Generic.KeyNotFoundException => (StatusCodes.Status404NotFound, "NotFound", "TABLE_NOT_FOUND"),
            MorphDB.Core.Exceptions.SchemaException se => (StatusCodes.Status400BadRequest, "SchemaError", se.ErrorCode),
            ArgumentException => (StatusCodes.Status400BadRequest, "BadRequest", "INVALID_ARGUMENT"),
            _ => (StatusCodes.Status500InternalServerError, "InternalError", "INTERNAL_ERROR")
        };
    }
}
