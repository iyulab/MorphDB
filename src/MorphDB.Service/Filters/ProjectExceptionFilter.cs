using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;

namespace MorphDB.Service.Filters;

/// <summary>
/// Answers, in one place, the request that never said which project it applies to.
/// <para>
/// Every schema and data endpoint needs a project, so every one of them used to carry its own catch
/// block for the case where none was given — and each block recognised the failure by looking for a
/// substring in the exception message. This filter is registered globally, so the endpoints can let
/// the exception travel and stay about their own subject.
/// </para>
/// </summary>
public sealed class ProjectExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MissingProjectException missing)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(new ErrorResponse
        {
            Error = "BadRequest",
            Message = missing.Message,
            Code = missing.ErrorCode
        });
        context.ExceptionHandled = true;
    }
}
