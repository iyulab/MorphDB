using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using MorphDB.Core.Abstractions;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <see cref="RequireProjectFilter"/> is the single choke point every project-scoped controller
/// shares (it carries <c>[RequireProject]</c>). The header half — absent or malformed — is covered end
/// to end by <c>ProjectScopeContractTests</c>; these tests pin the existence half at the unit level:
/// the action runs only for a project the repository returns, and never for one it does not.
/// </summary>
public class RequireProjectFilterTests
{
    private readonly Mock<IProjectRepository> _projectRepository = new();

    /// <summary>
    /// Builds the filter and its executing context off the same <see cref="DefaultHttpContext"/>, the
    /// way the real pipeline does — <see cref="HttpProjectContextAccessor"/> reads whatever
    /// <see cref="IHttpContextAccessor.HttpContext"/> currently holds, so a test that built two
    /// unrelated contexts would have the filter reading a project id that never matches the request
    /// it is judging.
    /// </summary>
    private (RequireProjectFilter Filter, ActionExecutingContext Context) CreateFilterAndContext(Guid? projectId)
    {
        var httpContext = new DefaultHttpContext();
        if (projectId is { } id)
        {
            httpContext.Request.Headers["X-Project-Id"] = id.ToString();
        }

        var filter = new RequireProjectFilter(
            new HttpProjectContextAccessor(new HttpContextAccessor { HttpContext = httpContext }),
            _projectRepository.Object);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        return (filter, context);
    }

    private static async Task<bool> RunAsync(RequireProjectFilter filter, ActionExecutingContext context)
    {
        var ran = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });
        return ran;
    }

    [Fact]
    public async Task An_existing_project_lets_the_action_run()
    {
        var projectId = Guid.NewGuid();
        _projectRepository
            .Setup(r => r.ExistsAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var (filter, context) = CreateFilterAndContext(projectId);

        var ran = await RunAsync(filter, context);

        ran.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    /// <summary>
    /// The repository answers "no" both for an id that was never created and for a deleted project,
    /// and both are the same answer to the caller: there is no such project to act on.
    /// </summary>
    [Fact]
    public async Task A_project_the_repository_does_not_return_is_refused_before_the_action()
    {
        var projectId = Guid.NewGuid();
        _projectRepository
            .Setup(r => r.ExistsAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var (filter, context) = CreateFilterAndContext(projectId);

        var ran = await RunAsync(filter, context);

        ran.Should().BeFalse();
        var result = context.Result.Should().BeOfType<JsonResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var body = result.Value.Should().BeOfType<ErrorResponse>().Subject;
        body.Code.Should().Be("PROJECT_NOT_FOUND");
    }

    [Fact]
    public async Task A_request_without_a_project_is_refused_without_a_lookup()
    {
        var (filter, context) = CreateFilterAndContext(projectId: null);

        var ran = await RunAsync(filter, context);

        ran.Should().BeFalse();
        context.Result.Should().BeOfType<JsonResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        _projectRepository.Verify(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
