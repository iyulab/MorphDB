using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Moq;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Exceptions;
using MorphDB.Core.Models;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Service.Services;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <see cref="RequireProjectFilter"/> is the single choke point every project-scoped controller
/// shares (it carries <c>[RequireProject]</c>). The pre-action half is already covered end to end by
/// <c>ProjectScopeContractTests</c>; these tests cover the half that has no integration-test
/// equivalent — re-diagnosing a <see cref="TableNotFoundException"/> that actually means the project
/// named in the request does not exist.
/// </summary>
public class RequireProjectFilterTests
{
    private readonly Mock<IProjectRepository> _projectRepository = new();

    /// <summary>
    /// Builds the filter and its exception context off the same <see cref="DefaultHttpContext"/>, the
    /// way the real pipeline does — <see cref="HttpProjectContextAccessor"/> reads whatever
    /// <see cref="IHttpContextAccessor.HttpContext"/> currently holds, so a test that built two
    /// unrelated contexts would have the filter reading a project id that never matches the request
    /// it is judging.
    /// </summary>
    private (RequireProjectFilter Filter, ExceptionContext Context) CreateFilterAndContext(
        Exception exception, Guid? projectId)
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
        var context = new ExceptionContext(actionContext, []) { Exception = exception };

        return (filter, context);
    }

    private static Project ExistingProject(Guid projectId) => new()
    {
        ProjectId = projectId,
        Name = "test",
        Slug = "test",
        SystemSchema = "p_test_sys",
        DataSchema = "p_test_dat"
    };

    [Fact]
    public async Task A_missing_table_inside_an_existing_project_is_left_alone()
    {
        var projectId = Guid.NewGuid();
        _projectRepository
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingProject(projectId));
        var (filter, context) = CreateFilterAndContext(new TableNotFoundException("invoices"), projectId);

        await filter.OnExceptionAsync(context);

        context.ExceptionHandled.Should().BeFalse(
            "the table really is missing from a project that exists — the original TABLE_NOT_FOUND answer stands");
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_table_inside_a_nonexistent_project_is_reported_as_the_project()
    {
        var projectId = Guid.NewGuid();
        _projectRepository
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);
        var (filter, context) = CreateFilterAndContext(new TableNotFoundException("invoices"), projectId);

        await filter.OnExceptionAsync(context);

        context.ExceptionHandled.Should().BeTrue();
        var result = context.Result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var body = result.Value.Should().BeOfType<ErrorResponse>().Subject;
        body.Code.Should().Be("PROJECT_NOT_FOUND");
    }

    /// <summary>
    /// A request whose project id is missing or malformed never reaches the action at all —
    /// <c>OnActionExecuting</c> stops it — so a <see cref="TableNotFoundException"/> arriving here
    /// with no resolvable id is nothing this filter can diagnose further, and it must not query the
    /// repository with a project id it does not have.
    /// </summary>
    [Fact]
    public async Task An_unresolvable_project_id_is_left_to_the_original_answer()
    {
        var (filter, context) = CreateFilterAndContext(new TableNotFoundException("invoices"), projectId: null);

        await filter.OnExceptionAsync(context);

        context.ExceptionHandled.Should().BeFalse();
        _projectRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_exception_that_is_not_a_missing_table_is_left_alone()
    {
        var (filter, context) = CreateFilterAndContext(new InvalidOperationException("boom"), Guid.NewGuid());

        await filter.OnExceptionAsync(context);

        context.ExceptionHandled.Should().BeFalse();
        _projectRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
