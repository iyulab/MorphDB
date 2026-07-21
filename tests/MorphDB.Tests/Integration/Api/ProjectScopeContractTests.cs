using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Every schema and data endpoint is scoped to a project, so every one of them has to answer the
/// request that did not name one — and until this was centralised they answered it three different
/// ways, each recognising the failure by searching an exception message for a substring.
/// <para>
/// These tests hold the answer to one shape. They are written across controllers on purpose: the
/// defect was not that any single answer was wrong, but that they disagreed, and a per-controller
/// test would have stayed green through the whole disagreement.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectScopeContractTests
{
    private readonly HttpClient _clientWithoutProject;

    public ProjectScopeContractTests(ApiIntegrationFixture fixture)
    {
        _clientWithoutProject = fixture.Api.CreateClientWithProject(Guid.Empty);
    }

    public static TheoryData<string> ScopedGetEndpoints => new()
    {
        "/api/schema/tables",
        "/api/views",
        "/api/webhooks",
    };

    [Theory]
    [MemberData(nameof(ScopedGetEndpoints))]
    public async Task An_endpoint_that_needs_a_project_says_so_the_same_way(string route)
    {
        var response = await _clientWithoutProject.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("MISSING_PROJECT",
            "the answer must carry a code callers can branch on, not just prose");
    }

    /// <summary>
    /// The write path is the one that has to be decided before the action runs, and this test is what
    /// measures that it is. Removing <c>[RequireProject]</c> from the data controller turns this red
    /// with a 500 — the request that forgot to name a project is reported as a server fault rather
    /// than as the incomplete request it is.
    /// </summary>
    [Fact]
    public async Task A_blanket_catch_does_not_swallow_the_answer_on_the_data_path()
    {
        var response = await _clientWithoutProject.PostAsJsonAsync(
            "/api/data/anything",
            new { name = "value" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error!.Code.Should().Be("MISSING_PROJECT");
    }
}
