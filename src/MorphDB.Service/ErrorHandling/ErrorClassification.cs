using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;
using Npgsql;

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
            Message = status >= StatusCodes.Status500InternalServerError ? "An unexpected error occurred"
                : exception is PostgresException pg ? DataErrorMessage(pg)
                : exception.Message,
            Details = exception is ValidationException { Field: { } field, Reason: { } reason }
                ? new Dictionary<string, string[]> { [field] = [reason] }
                : null,
        });
    }

    /// <summary>
    /// What a data exception says to the caller: the kind of error and that a computed value raised it.
    /// PostgreSQL's own text is not shown -- it can quote a value or carry a physical name.
    /// </summary>
    private static string DataErrorMessage(PostgresException pg)
    {
        var kind = pg.SqlState switch
        {
            PostgresErrorCodes.DivisionByZero => "division by zero",
            PostgresErrorCodes.NumericValueOutOfRange => "a number out of range",
            PostgresErrorCodes.InvalidTextRepresentation or PostgresErrorCodes.InvalidDatetimeFormat => "a value that is not of its type",
            _ => "a data error",
        };
        return $"A value in this request, or one computed for it, raised {kind}. A formula column or a view expression raises it "
            + "for the rows it cannot be computed for, and a read that meets such a row fails as a whole.";
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
            RowReferencedException r => (StatusCodes.Status409Conflict, "Conflict", r.ErrorCode),
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
            // A data exception (SQLSTATE class 22) that escaped a request was raised by a value the
            // database computed -- a formula column or a view expression dividing by zero for a row, a
            // number out of range -- never by a fault of this service. It is said as such, once, here, so
            // every door that reads (REST, GraphQL) answers it the same way.
            PostgresException { SqlState: ['2', '2', ..] } => (StatusCodes.Status400BadRequest, "DataError", "DATA_EXCEPTION"),
            _ => (StatusCodes.Status500InternalServerError, "InternalError", "INTERNAL_ERROR")
        };
    }
}
