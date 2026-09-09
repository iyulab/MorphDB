using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using MorphDB.Core.Exceptions;
using MorphDB.Service.Filters;
using MorphDB.Service.Services;

namespace MorphDB.Tests.Unit;

/// <summary>
/// <see cref="HttpProjectContextAccessor"/> is the one place that decides, from a raw
/// <c>X-Project-Id</c> header, which of three states a request is in: a usable id, nothing sent, or
/// something sent that is not a GUID. <see cref="RequireProjectAttribute"/> and every GraphQL
/// resolver that reads <c>ProjectId</c> directly depend on that third state being distinguishable
/// from the second — these tests pin the distinction at its source.
/// </summary>
public class ProjectContextAccessorTests
{
    private static HttpProjectContextAccessor Accessor(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
        {
            httpContext.Request.Headers["X-Project-Id"] = headerValue;
        }

        return new HttpProjectContextAccessor(new HttpContextAccessor { HttpContext = httpContext });
    }

    [Fact]
    public void No_header_is_missing_not_malformed()
    {
        var accessor = Accessor(headerValue: null);

        accessor.ProjectIdOrNull.Should().BeNull();
        accessor.MalformedProjectIdHeaderValue.Should().BeNull();
    }

    [Fact]
    public void A_wellformed_guid_resolves_and_is_not_malformed()
    {
        var projectId = Guid.NewGuid();
        var accessor = Accessor(projectId.ToString());

        accessor.ProjectIdOrNull.Should().Be(projectId);
        accessor.MalformedProjectIdHeaderValue.Should().BeNull();
    }

    [Fact]
    public void A_value_that_does_not_parse_as_a_guid_is_malformed_not_missing()
    {
        var accessor = Accessor("nonexistent-xyz");

        accessor.ProjectIdOrNull.Should().BeNull();
        accessor.MalformedProjectIdHeaderValue.Should().Be("nonexistent-xyz");
    }

    /// <summary>
    /// Guid.Empty parses fine but names no project a caller could own — it stays in the same
    /// "nothing usable" bucket as an absent header, which is also what the test fixtures rely on to
    /// simulate "no project" without omitting the header (see <c>ProjectScopeContractTests</c>).
    /// </summary>
    [Fact]
    public void The_empty_guid_is_missing_not_malformed()
    {
        var accessor = Accessor(Guid.Empty.ToString());

        accessor.ProjectIdOrNull.Should().BeNull();
        accessor.MalformedProjectIdHeaderValue.Should().BeNull();
    }

    [Fact]
    public void ProjectId_throws_MalformedProjectIdException_when_the_header_failed_to_parse()
    {
        var accessor = Accessor("nonexistent-xyz");

        var act = () => accessor.ProjectId;

        act.Should().Throw<MalformedProjectIdException>()
            .Which.Message.Should().Contain("nonexistent-xyz");
    }

    [Fact]
    public void ProjectId_throws_MissingProjectException_when_nothing_was_sent()
    {
        var accessor = Accessor(headerValue: null);

        var act = () => accessor.ProjectId;

        act.Should().Throw<MissingProjectException>();
    }
}
