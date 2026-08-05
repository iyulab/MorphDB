using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A caller may choose the project id when creating a project. The capability was declared on the
/// core request and honoured all the way to the insert, but the HTTP request object had no such
/// field and the controller built its own — so the promise was unreachable from the only place a
/// caller stands.
/// <para>
/// The tests below are written against the reason it exists: a deployment whose manifests are
/// authored before anything runs cannot write down an id that is generated at startup. That makes
/// the second creation attempt part of the contract, not an edge case — a start-up step re-runs and
/// needs to tell "already there" apart from a failure.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectIdSelectionTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ProjectIdSelectionTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task A_project_is_created_under_the_id_the_request_chose()
    {
        var chosen = Guid.NewGuid();

        var response = await CreateAsync(chosen);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Id.Should().Be(chosen);
    }

    /// <summary>
    /// The echoed id is not evidence on its own — a service that ignored the field could still
    /// return what it was sent. Only a request scoped by that id reaches the schemas it names.
    /// </summary>
    [Fact]
    public async Task The_chosen_id_scopes_requests_to_the_project_it_created()
    {
        var chosen = Guid.NewGuid();
        (await CreateAsync(chosen)).EnsureSuccessStatusCode();

        using var scopedClient = _fixture.Api.CreateClientWithProject(chosen);
        var scoped = await scopedClient.GetAsync("/api/schema/tables");

        scoped.StatusCode.Should().Be(HttpStatusCode.OK, await scoped.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Omitting_the_id_still_generates_one()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new { Name = UniqueName() });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Id.Should().NotBeEmpty();
    }

    /// <summary>
    /// Under a different name, so the slug is free and this is the id colliding rather than the slug.
    /// Without a check of its own the insert would violate the primary key, which reaches the caller
    /// as an internal error — the one answer a repeated start-up step cannot act on.
    /// </summary>
    [Fact]
    public async Task Reusing_a_chosen_id_is_a_conflict_rather_than_an_internal_error()
    {
        var chosen = Guid.NewGuid();
        (await CreateAsync(chosen)).EnsureSuccessStatusCode();

        var again = await CreateAsync(chosen);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync());
        (await again.Content.ReadFromJsonAsync<ErrorResponse>())!.Code.Should().Be("DUPLICATE_PROJECT_ID");
    }

    /// <summary>
    /// Deleting a project sets its status; the row keeps the id. Reading availability the way slugs
    /// read it would call the id free and leave the insert to fail underneath.
    /// </summary>
    [Fact]
    public async Task A_deleted_project_still_holds_its_id()
    {
        var chosen = Guid.NewGuid();
        (await CreateAsync(chosen)).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/api/projects/{chosen}")).EnsureSuccessStatusCode();

        var again = await CreateAsync(chosen);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync());
        (await again.Content.ReadFromJsonAsync<ErrorResponse>())!.Code.Should().Be("DUPLICATE_PROJECT_ID");
    }

    /// <summary>
    /// The availability check is a check-then-insert, so concurrent callers can both pass it and one
    /// of them meets the primary key instead. Answering that one differently would make the contract
    /// depend on timing — and a start-up step that re-runs is precisely the caller who races.
    /// </summary>
    [Fact]
    public async Task Racing_callers_are_both_answered_in_terms_of_the_id()
    {
        var chosen = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(chosen)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1,
            "the id names one project, however many callers asked for it");

        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
            (await refused.Content.ReadFromJsonAsync<ErrorResponse>())!.Code.Should().Be("DUPLICATE_PROJECT_ID");
        }
    }

    private Task<HttpResponseMessage> CreateAsync(Guid projectId) =>
        _client.PostAsJsonAsync("/api/projects", new { ProjectId = projectId, Name = UniqueName() });

    private static string UniqueName() => $"pidsel_{Guid.NewGuid():N}"[..28];
}
