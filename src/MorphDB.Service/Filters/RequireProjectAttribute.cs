using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Filters;

/// <summary>
/// Marks a controller whose every action is scoped to a project, and answers the request that did not
/// say which one before the action runs — or that named one that does not exist, once the action's
/// own answer says so.
/// <para>
/// Deciding the first ahead of the action matters for a reason beyond tidiness: several of these
/// actions end in a blanket <c>catch (Exception)</c>, which would swallow the failure and return a
/// generic 400 with no error code. A filter that runs first cannot be caught by the code it precedes.
/// </para>
/// <para>
/// What it replaces: each action used to carry its own catch block recognising the failure by
/// searching the exception message for the header name — which made the wording of a message part of
/// the public contract, and let any unrelated exception mentioning the same header take that branch.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RequireProjectAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => true;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        new RequireProjectFilter(
            serviceProvider.GetRequiredService<IProjectContextAccessor>(),
            serviceProvider.GetRequiredService<IProjectRepository>());
}

internal sealed class RequireProjectFilter : IActionFilter, IAsyncExceptionFilter
{
    private readonly IProjectContextAccessor _projectContext;
    private readonly IProjectRepository _projectRepository;

    public RequireProjectFilter(IProjectContextAccessor projectContext, IProjectRepository projectRepository)
    {
        _projectContext = projectContext;
        _projectRepository = projectRepository;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (_projectContext.ProjectIdOrNull is not null)
        {
            return;
        }

        // The exception type owns the wording — duplicating the literal here once let the two
        // drift, and the filter briefly advertised an API key the server never asks for. Which type
        // to instantiate follows the accessor's own distinction: a header that failed to parse said
        // something and gets told what; a request that said nothing gets asked to say something.
        MorphDbException error = _projectContext.MalformedProjectIdHeaderValue is { } raw
            ? new MalformedProjectIdException(raw)
            : new MissingProjectException();
        context.Result = new BadRequestObjectResult(new ErrorResponse
        {
            Error = "BadRequest",
            Message = error.Message,
            Code = error.ErrorCode
        });
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>
    /// A table lookup inside a project that does not exist answers <c>TABLE_NOT_FOUND</c> — true of
    /// the table, but not the mistake a caller who mistyped a project id made. Checked only here, on
    /// the error path a request already took, so a request whose project and table both exist pays
    /// no extra query.
    /// </summary>
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.ExceptionHandled || context.Exception is not TableNotFoundException)
        {
            return;
        }

        // OnActionExecuting already turned an absent or malformed project id away before the action
        // ran, so reaching here with no id would mean the action ran without one — nothing this
        // filter can diagnose further.
        if (_projectContext.ProjectIdOrNull is not { } projectId)
        {
            return;
        }

        var project = await _projectRepository.GetByIdAsync(projectId, context.HttpContext.RequestAborted);
        if (project is not null)
        {
            return;
        }

        var error = new ProjectNotFoundException(projectId);
        context.ExceptionHandled = true;
        context.Result = new NotFoundObjectResult(new ErrorResponse
        {
            Error = "NotFound",
            Message = error.Message,
            Code = error.ErrorCode
        });
    }
}
