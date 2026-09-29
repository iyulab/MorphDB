using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    /// <summary>
    /// Deleting a project used to drop its (empty) schemas and leave everything else serving: its
    /// tables and rows stayed where they were, and a request scoped to the deleted id kept reading,
    /// writing, and creating tables. A deleted project is no project — every scoped route says so.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_is_refused_on_the_routes_it_used_to_answer()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await _fixture.Api.CreateClientWithNewProjectAsync(ct);
        var projectId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Project-Id").Single());
        var table = $"del_{Guid.NewGuid():N}"[..24];
        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await _fixture.Api.Client.DeleteAsync($"/api/projects/{projectId}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var responses = new[]
        {
            await client.PostAsJsonAsync($"/api/data/{table}", new Dictionary<string, object?> { ["label"] = "after" }, ct),
            await client.GetAsync($"/api/data/{table}", ct),
            await client.GetAsync("/api/schema/tables", ct),
            await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
            {
                Name = $"del2_{Guid.NewGuid():N}"[..24],
                Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
            }, ct),
        };

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, response.RequestMessage!.RequestUri!.ToString());
            (await response.Content.ReadFromJsonAsync<ErrorResponse>(ct))!.Code.Should().Be("PROJECT_NOT_FOUND");
        }
    }

    /// <summary>
    /// Schema writes never looked the project up, so a mistyped id created tables in a project that
    /// did not exist — and a service configured with that id ran against it without an error.
    /// </summary>
    [Fact]
    public async Task A_project_that_was_never_created_cannot_create_tables()
    {
        using var client = _fixture.Api.CreateClientWithProject(Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = $"ghost_{Guid.NewGuid():N}"[..24],
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!.Code
            .Should().Be("PROJECT_NOT_FOUND");
    }

    /// <summary>
    /// GraphQL and OData read the project id themselves rather than through the REST filter, so a
    /// deleted project's tables stayed readable and writable on those two surfaces after every REST
    /// route had stopped serving them.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_is_refused_on_graphql_and_odata_too()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await _fixture.Api.CreateClientWithNewProjectAsync(ct);
        var projectId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Project-Id").Single());
        var table = $"gdel_{Guid.NewGuid():N}"[..24];
        (await client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "label", Type = "text", Nullable = true }]
        }, ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _fixture.Api.Client.DeleteAsync($"/api/projects/{projectId}", ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var query in new[]
        {
            "query { tables { name } }",
            $$"""mutation { createRecord(table: "{{table}}", data: {label: "after"}) { success } }""",
        })
        {
            var response = await client.PostAsJsonAsync("/graphql", new { query }, ct);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            body.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue(query);
            errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("PROJECT_NOT_FOUND", query);
            body.RootElement.TryGetProperty("data", out var data).Should().BeFalse(
                $"no field may resolve for a project that is gone: {query} -> {data}");
        }

        foreach (var route in new[] { $"/odata/{table}", "/odata/$metadata" })
        {
            var odata = await client.GetAsync(route, ct);
            odata.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{route} — the XML-only $metadata must not turn the refusal into 406");
            (await odata.Content.ReadFromJsonAsync<ErrorResponse>(ct))!.Code.Should().Be("PROJECT_NOT_FOUND");
        }
    }
}
