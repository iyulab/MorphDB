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

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await response.Content.ReadFromJsonAsync<ProjectApiResponse>(TestContext.Current.CancellationToken))!.Id.Should().Be(chosen);
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
        var scoped = await scopedClient.GetAsync("/api/schema/tables", TestContext.Current.CancellationToken);

        scoped.StatusCode.Should().Be(HttpStatusCode.OK, await scoped.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Omitting_the_id_still_generates_one()
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new { Name = UniqueName() }, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<ProjectApiResponse>(TestContext.Current.CancellationToken))!.Id.Should().NotBeEmpty();
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

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await again.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!.Code.Should().Be("DUPLICATE_PROJECT_ID");
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
        (await _client.DeleteAsync($"/api/projects/{chosen}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var again = await CreateAsync(chosen);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await again.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!.Code.Should().Be("DUPLICATE_PROJECT_ID");
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
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            (await refused.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!.Code.Should().Be("DUPLICATE_PROJECT_ID");
        }
    }

    /// <summary>
    /// Schema names use only the first eight hex digits of the id, so an id that differs from a taken
    /// one only further along asks for its schemas. That used to reach the caller as an internal error
    /// from the unique constraint — an answer that names neither the cause nor what to change.
    /// </summary>
    [Fact]
    public async Task An_id_sharing_its_first_eight_digits_with_a_project_is_a_conflict_naming_that_project()
    {
        var first = Guid.NewGuid();
        (await CreateAsync(first)).EnsureSuccessStatusCode();

        var sibling = SiblingOf(first);
        var response = await CreateAsync(sibling);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var error = (await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!;
        error.Code.Should().Be("DUPLICATE_PROJECT_SCHEMA");
        error.Message.Should().Contain(first.ToString()).And.Contain("first 8 hex digits");
        (await _client.GetAsync($"/api/projects/{sibling}", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The same collision met by racing callers passes the check and lands on the constraint instead;
    /// the answer must not depend on which path refused them.
    /// </summary>
    [Fact]
    public async Task Racing_ids_that_share_their_schemas_are_answered_in_terms_of_the_schema()
    {
        var first = Guid.NewGuid();
        var ids = new[] { first, SiblingOf(first), SiblingOf(SiblingOf(first)) };

        var responses = await Task.WhenAll(ids.Select(CreateAsync));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "the schemas can hold one project");
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            (await refused.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken))!.Code.Should().Be("DUPLICATE_PROJECT_SCHEMA");
        }
    }

    /// <summary>The same first eight hex digits, a different id: the last digit is rotated.</summary>
    private static Guid SiblingOf(Guid id)
    {
        var text = id.ToString();
        var last = Convert.ToInt32(text[^1].ToString(), 16);
        return Guid.Parse(text[..^1] + ((last + 1) % 16).ToString("x", System.Globalization.CultureInfo.InvariantCulture));
    }

    private Task<HttpResponseMessage> CreateAsync(Guid projectId) =>
        _client.PostAsJsonAsync("/api/projects", new { ProjectId = projectId, Name = UniqueName() });

    private static string UniqueName() => $"pidsel_{Guid.NewGuid():N}"[..28];
}
