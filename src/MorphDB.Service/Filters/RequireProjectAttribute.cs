using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Service.Filters;

/// <summary>
/// Marks a controller whose every action is scoped to a project, and answers — before the action
/// runs — the request that did not say which project, or that named one that does not exist or has
/// been deleted.
/// <para>
/// Deciding ahead of the action matters for a reason beyond tidiness: several of these actions end in
/// a blanket <c>catch (Exception)</c>, which would swallow the failure and return a generic 400 with
/// no error code. A filter that runs first cannot be caught by the code it precedes.
/// </para>
/// <para>
/// The check is <see cref="IProjectRepository.ExistsAsync"/>: an instance forgets its cached "yes"
/// when it changes a project's status, so a delete takes effect there at once, and within seconds on
/// any other instance serving the same database. Without the check, schema writes never looked the project up at all, so an id that was never
/// created — or one that had been deleted — could still create tables and write rows.
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

internal sealed class RequireProjectFilter : IAsyncActionFilter
{
    private readonly IProjectContextAccessor _projectContext;
    private readonly IProjectRepository _projectRepository;

    public RequireProjectFilter(IProjectContextAccessor projectContext, IProjectRepository projectRepository)
    {
        _projectContext = projectContext;
        _projectRepository = projectRepository;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (_projectContext.ProjectIdOrNull is not { } projectId)
        {
            // The exception type owns the wording — duplicating the literal here once let the two
            // drift, and the filter briefly advertised an API key the server never asks for. Which
            // type to instantiate follows the accessor's own distinction: a header that failed to
            // parse said something and gets told what; a request that said nothing gets asked to say
            // something.
            MorphDbException missing = _projectContext.MalformedProjectIdHeaderValue is { } raw
                ? new MalformedProjectIdException(raw)
                : new MissingProjectException();
            context.Result = new BadRequestObjectResult(new ErrorResponse
            {
                Error = "BadRequest",
                Message = missing.Message,
                Code = missing.ErrorCode
            });
            return;
        }

        // A deleted project's rows are still in its tables, and must not stay reachable.
        if (!await _projectRepository.ExistsAsync(projectId, context.HttpContext.RequestAborted))
        {
            var notFound = new ProjectNotFoundException(projectId);
            context.Result = new NotFoundObjectResult(new ErrorResponse
            {
                Error = "NotFound",
                Message = notFound.Message,
                Code = notFound.ErrorCode
            });
            return;
        }

        await next();
    }
}
