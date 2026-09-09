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
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _clientWithoutProject;

    public ProjectScopeContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
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
        var response = await _clientWithoutProject.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
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
            new { name = "value" }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        error!.Code.Should().Be("MISSING_PROJECT");
    }

    /// <summary>
    /// A header that was sent but does not parse as a GUID used to collapse into the same
    /// <c>MISSING_PROJECT</c> answer as no header at all — telling a caller who mistyped their
    /// project id to send the header it had already sent.
    /// </summary>
    [Fact]
    public async Task A_header_that_is_not_a_guid_is_told_so_rather_than_asked_to_resend_it()
    {
        using var client = _fixture.Api.CreateClientWithRawProjectHeader("nonexistent-xyz");

        var response = await client.GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        error!.Code.Should().Be("INVALID_PROJECT_ID");
        error.Message.Should().Contain("nonexistent-xyz");
    }

    /// <summary>
    /// A data request scoped to a well-formed GUID that names no project used to answer
    /// <c>TABLE_NOT_FOUND</c> — true of the table (the project has no tables at all), but the wrong
    /// diagnosis for a caller who mistyped their project id and would otherwise go looking for a
    /// table that was never missing.
    /// </summary>
    [Fact]
    public async Task A_data_request_against_a_nonexistent_project_names_the_project_not_the_table()
    {
        using var client = _fixture.Api.CreateClientWithProject(Guid.NewGuid());

        var response = await client.GetAsync("/api/data/invoices", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        error!.Code.Should().Be("PROJECT_NOT_FOUND");
    }
}
