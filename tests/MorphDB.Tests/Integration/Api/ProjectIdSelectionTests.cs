using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Core.Abstractions;
using MorphDB.Core.Models;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;
using Npgsql;

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
    /// Schema names used to come from only the first eight hex digits of the id, so an id differing
    /// from a taken one only further along asked for its schemas and was refused — and time-ordered
    /// ids (UUIDv7) share those digits for about a minute. A new project's schemas are named from the
    /// whole id, so such ids are simply two projects.
    /// </summary>
    [Fact]
    public async Task Ids_sharing_their_first_eight_digits_are_two_projects_with_their_own_schemas()
    {
        var first = Guid.NewGuid();
        var sibling = SiblingOf(first);

        var responses = await Task.WhenAll(new[] { first, sibling }.Select(CreateAsync));

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        var firstNames = await RecordedSchemaNamesAsync(first);
        var siblingNames = await RecordedSchemaNamesAsync(sibling);
        firstNames.SystemSchema.Should().Be($"p_{first:N}_sys");
        siblingNames.SystemSchema.Should().Be($"p_{sibling:N}_sys");

        foreach (var id in new[] { first, sibling })
        {
            var health = await _client.GetFromJsonAsync<SchemaHealthApiResponse>($"/api/projects/{id}/health", TestContext.Current.CancellationToken);
            health!.IsHealthy.Should().BeTrue($"project {id} has both of its own schemas");
        }
    }

    /// <summary>
    /// A project created before the naming rule changed keeps the eight-digit schemas it was given —
    /// nothing renames a deployed database's schemas. Every operation must therefore read the names
    /// recorded with the project; one that computed them from the id would look for schemas that do
    /// not exist (health: missing) and, on delete, leave the real ones behind.
    /// </summary>
    [Fact]
    public async Task A_project_recorded_under_the_earlier_eight_digit_names_is_operated_on_through_them()
    {
        var legacy = Guid.NewGuid();
        var legacyNames = new SchemaNames($"p_{legacy:N}"[..10] + "_sys", $"p_{legacy:N}"[..10] + "_dat");
        await RecordLegacyProjectAsync(legacy, legacyNames);

        var health = await _client.GetFromJsonAsync<SchemaHealthApiResponse>($"/api/projects/{legacy}/health", TestContext.Current.CancellationToken);
        health!.IsHealthy.Should().BeTrue(string.Join("; ", health.Issues.Select(i => i.Message)));

        var deleted = await _client.DeleteAsync($"/api/projects/{legacy}", TestContext.Current.CancellationToken);
        deleted.IsSuccessStatusCode.Should().BeTrue(await deleted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        var remaining = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_namespace WHERE nspname IN (@System, @Data)",
            new { System = legacyNames.SystemSchema, Data = legacyNames.DataSchema });
        remaining.Should().Be(0, "deleting the project drops the schemas it actually has");
    }

    /// <summary>
    /// Stands up a project the way a pre-change version left it: provisioned schemas under the
    /// eight-digit names and a row recording them. The schemas come from a project provisioned by
    /// the current code, renamed — so their contents are exactly what provisioning creates.
    /// </summary>
    private async Task RecordLegacyProjectAsync(Guid legacy, SchemaNames legacyNames)
    {
        var template = Guid.NewGuid();
        (await CreateAsync(template)).EnsureSuccessStatusCode();
        var templateNames = await RecordedSchemaNamesAsync(template);

        await using var connection = new NpgsqlConnection(_fixture.Postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteAsync($"""ALTER SCHEMA "{templateNames.SystemSchema}" RENAME TO "{legacyNames.SystemSchema}" """, transaction: transaction);
        await connection.ExecuteAsync($"""ALTER SCHEMA "{templateNames.DataSchema}" RENAME TO "{legacyNames.DataSchema}" """, transaction: transaction);
        await connection.ExecuteAsync(
            "UPDATE morphdb._morph_projects SET status = @Deleted WHERE project_id = @Template",
            new { Deleted = (int)ProjectStatus.Deleted, Template = template },
            transaction);
        await connection.ExecuteAsync(
            """
            INSERT INTO morphdb._morph_projects (project_id, name, slug, system_schema, data_schema, settings, status, created_at, updated_at)
            VALUES (@Id, @Name, @Slug, @System, @Data, '{}'::jsonb, @Active, NOW(), NOW())
            """,
            new
            {
                Id = legacy,
                Name = UniqueName(),
                Slug = $"legacy-{legacy:N}",
                System = legacyNames.SystemSchema,
                Data = legacyNames.DataSchema,
                Active = (int)ProjectStatus.Active,
            },
            transaction);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }

    private async Task<SchemaNames> RecordedSchemaNamesAsync(Guid projectId)
    {
        using var scope = _fixture.Api.Services.CreateScope();
        var names = await scope.ServiceProvider.GetRequiredService<IProjectRepository>()
            .GetSchemaNamesAsync(projectId, TestContext.Current.CancellationToken);
        return names ?? throw new InvalidOperationException($"no project recorded under {projectId}");
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
