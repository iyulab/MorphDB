using HotChocolate;
using HotChocolate.Execution;
using MorphDB.Service.ErrorHandling;

namespace MorphDB.Service.GraphQL;

/// <summary>
/// A caller's mistake reaches a GraphQL client the way it reaches a REST one: with the error's own
/// message and the code REST answers with (<c>INVALID_ARGUMENT</c>, <c>TABLE_NOT_FOUND</c>,
/// <c>COLUMN_NOT_FOUND</c>, …).
/// <para>
/// Without this an exception a resolver let escape became HotChocolate's «Unexpected Execution Error»
/// outside Development — an unknown operator or column was indistinguishable from a server defect.
/// Anything <see cref="ErrorClassification"/> calls ours is left as it was: unexplained, because its
/// text is not contract.
/// </para>
/// <para>
/// Those errors are logged here. HotChocolate reports an exception it did not classify to the client without
/// a cause and writes nothing itself, so the server's own log said only that the request answered <c>200</c> —
/// the REST pipeline logs every 5xx with its exception, and this door held to the same.
/// </para>
/// </summary>
internal sealed partial class CallerErrorFilter(ILogger<CallerErrorFilter> logger) : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is not { } exception || error.Code is not null)
        {
            return error;
        }

        var (status, _, code) = ErrorClassification.Classify(exception);
        if (status >= StatusCodes.Status500InternalServerError)
        {
            LogUnexpected(exception, error.Path?.Print());
            return error;
        }

        return ErrorBuilder.FromError(error)
            .SetMessage(exception.Message)
            .SetCode(code)
            .SetExtension("status", status)
            .Build();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error resolving GraphQL field {Path}")]
    private partial void LogUnexpected(Exception exception, string? path);
}
