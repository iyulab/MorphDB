using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Filters;

/// <summary>
/// Marks a controller whose every action is scoped to a project, and answers the request that did not
/// say which one before the action runs.
/// <para>
/// Deciding this ahead of the action matters for a reason beyond tidiness: several of these actions
/// end in a blanket <c>catch (Exception)</c>, which would swallow the failure and return a generic
/// 400 with no error code. A filter that runs first cannot be caught by the code it precedes.
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
        new RequireProjectFilter(serviceProvider.GetRequiredService<IProjectContextAccessor>());
}

internal sealed class RequireProjectFilter : IActionFilter
{
    private readonly IProjectContextAccessor _projectContext;

    public RequireProjectFilter(IProjectContextAccessor projectContext) => _projectContext = projectContext;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (_projectContext.ProjectIdOrNull is not null)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(new ErrorResponse
        {
            Error = "BadRequest",
            Message = "This request must say which project it applies to. Send a valid API key, or an X-Project-Id header.",
            Code = "MISSING_PROJECT"
        });
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
