using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// Exercises the client's project surface against a running server, because the interesting
/// questions about it are not shape questions.
/// <para>
/// Every other client is scoped to one project through the <c>X-Project-Id</c> header, which the
/// client sets once and sends on everything. Project calls are the exception: they name the project
/// in the route, and one of them — creating the first project — necessarily happens before there is
/// an id to send. So the surface has two claims worth holding: it works with no project id set, and
/// it is not quietly scoped by one that is.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectClientTests
{
    private readonly ApiIntegrationFixture _fixture;

    public ProjectClientTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    private MorphDBClient ClientWithoutProject()
    {
        var http = new HttpClient(_fixture.Api.CreateHandler()) { BaseAddress = _fixture.Api.BaseAddress };
        return new MorphDBClient(http);
    }

    private MorphDBClient ClientScopedToTheFixtureProject()
    {
        var client = ClientWithoutProject();
        client.SetProjectId(_fixture.Api.ProjectId);
        return client;
    }

    [Fact]
    public async Task A_project_can_be_created_read_updated_and_deleted_without_ever_setting_a_project_id()
    {
        await using var client = ClientWithoutProject();
        var id = Guid.NewGuid();
        var slug = $"sdk-{id:N}"[..20];

        var created = await client.Projects.CreateAsync(new CreateProjectRequest
        {
            ProjectId = id,
            Name = "SDK round trip",
            Slug = slug,
        });

        try
        {
            created.Id.Should().Be(id, "the server honours a chosen id, and the client has to pass it through");
            created.Slug.Should().Be(slug);
            created.Status.Should().Be("active");

            (await client.Projects.GetAsync(id))!.Name.Should().Be("SDK round trip");
            (await client.Projects.GetBySlugAsync(slug))!.Id.Should().Be(id);

            var renamed = await client.Projects.UpdateAsync(id, new UpdateProjectRequest { Name = "Renamed" });
            renamed.Name.Should().Be("Renamed");
            renamed.Slug.Should().Be(slug, "renaming does not re-derive the slug an id was published under");

            var stats = await client.Projects.GetStatsAsync(id);
            stats.ProjectId.Should().Be(id);

            var health = await client.Projects.GetHealthAsync(id);
            health.ProjectId.Should().Be(id);
        }
        finally
        {
            await client.Projects.DeleteAsync(id);
        }

        (await client.Projects.GetAsync(id)).Should().BeNull("a deleted project is gone, not merely unreadable");
    }

    [Fact]
    public async Task A_project_id_on_the_client_does_not_scope_the_project_calls()
    {
        // The header is set to one project and the call is about another. If project routes were
        // scoped by the header the way data routes are, this would answer about the wrong project
        // or refuse -- and a caller managing several projects from one client would find out at
        // run time.
        await using var client = ClientScopedToTheFixtureProject();
        var other = Guid.NewGuid();

        var created = await client.Projects.CreateAsync(new CreateProjectRequest
        {
            ProjectId = other,
            Name = "Not the scoped one",
            Slug = $"other-{other:N}"[..20],
        });

        try
        {
            created.Id.Should().Be(other);
            (await client.Projects.GetAsync(other))!.Id.Should().Be(other);
            (await client.Projects.GetAsync(_fixture.Api.ProjectId))!.Id.Should().Be(_fixture.Api.ProjectId);
        }
        finally
        {
            await client.Projects.DeleteAsync(other);
        }
    }

    [Fact]
    public async Task Listing_projects_returns_the_ones_that_exist()
    {
        await using var client = ClientWithoutProject();

        var page = await client.Projects.ListAsync(pageSize: 100);

        page.Data.Should().NotBeEmpty("the fixture provisions a project before the server starts");
        page.Data.Should().Contain(p => p.Id == _fixture.Api.ProjectId);
        page.Pagination.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Asking_for_a_project_that_is_not_there_answers_null_rather_than_throwing()
    {
        await using var client = ClientWithoutProject();

        (await client.Projects.GetAsync(Guid.NewGuid())).Should().BeNull();
        (await client.Projects.GetBySlugAsync($"absent-{Guid.NewGuid():N}"[..20])).Should().BeNull();
    }

    [Fact]
    public async Task Creating_a_project_under_a_taken_id_is_a_conflict_the_caller_can_branch_on()
    {
        await using var client = ClientWithoutProject();
        var id = Guid.NewGuid();
        var request = new CreateProjectRequest
        {
            ProjectId = id,
            Name = "First",
            Slug = $"taken-{id:N}"[..20],
        };

        await client.Projects.CreateAsync(request);

        try
        {
            var second = async () => await client.Projects.CreateAsync(new CreateProjectRequest
            {
                ProjectId = id,
                Name = "Second",
                Slug = $"free-{id:N}"[..20],
            });

            // The typed exception carries the server's own code, which is the half a status alone
            // does not give: a conflict here can be the id or the slug, and they are different bugs.
            (await second.Should().ThrowAsync<MorphDBConflictException>())
                .Which.ErrorCode.Should().Be("DUPLICATE_PROJECT_ID");
        }
        finally
        {
            await client.Projects.DeleteAsync(id);
        }
    }

    [Fact]
    public async Task Updating_settings_replaces_them_and_the_client_makes_that_visible()
    {
        // The contract this is guarding is the server's, and ProjectSettingsReplacementTests already
        // holds it over HTTP. What is new is that a caller can now reach it through the client, and
        // the client's own defaults are what decide the stored result. If they ever drift from the
        // server's, this is where a project ends up configured differently from what was asked for.
        await using var client = ClientWithoutProject();
        var id = Guid.NewGuid();

        await client.Projects.CreateAsync(new CreateProjectRequest
        {
            ProjectId = id,
            Name = "Settings",
            Slug = $"settings-{id:N}"[..20],
            Settings = new ProjectSettings
            {
                DefaultLocale = "en-GB",
                EnableAuditLog = false,
                DefaultEnforceOnWrite = false,
            },
        });

        try
        {
            var stated = (await client.Projects.GetAsync(id))!.Settings!;
            stated.DefaultLocale.Should().Be("en-GB");
            stated.EnableAuditLog.Should().BeFalse();
            stated.DefaultEnforceOnWrite.Should().BeFalse();

            // Now change one field the way a caller reaching for a partial update would.
            await client.Projects.UpdateAsync(id, new UpdateProjectRequest
            {
                Settings = new ProjectSettings { Timezone = "Europe/London" },
            });

            var after = (await client.Projects.GetAsync(id))!.Settings!;
            after.Timezone.Should().Be("Europe/London");
            after.DefaultLocale.Should().BeNull("settings are stored by replacement, not merged");
            after.DefaultEnforceOnWrite.Should().BeTrue(
                "the default is enforcement, so a settings object that does not turn it off turns it " +
                "back on -- the client cannot hide this, it can only make sure the object sent is the " +
                "object the caller built");
        }
        finally
        {
            await client.Projects.DeleteAsync(id);
        }
    }
}
