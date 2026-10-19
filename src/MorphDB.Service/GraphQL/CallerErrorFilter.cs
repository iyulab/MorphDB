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
/// </summary>
internal sealed class CallerErrorFilter : IErrorFilter
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
            return error;
        }

        return ErrorBuilder.FromError(error)
            .SetMessage(exception.Message)
            .SetCode(code)
            .SetExtension("status", status)
            .Build();
    }
}
