using System.Net;
using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// A project's settings are replaced whole, not merged. The behaviour was deliberate and left as it
/// was; what it lacked was anything holding it, so it was described in three documents and enforced
/// by none of them.
/// <para>
/// The gap is easy to miss because every existing test sends one setting at a time — a merge would
/// pass all of them. Only a project that already carries a second setting can tell the two apart,
/// and that is precisely the caller who loses data to the difference.
/// </para>
/// <para>
/// This is the shape a caller has to be warned about rather than protected from: refusing partial
/// settings would be a different contract, and quietly merging them would make the stored object
/// depend on which fields a client's serializer chose to omit.
/// </para>
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectSettingsReplacementTests
{
    private readonly HttpClient _client;

    public ProjectSettingsReplacementTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task Patching_one_setting_returns_the_others_to_their_defaults()
    {
        var projectId = await CreateProjectAsync(new ProjectSettingsApiModel
        {
            AuditLogRetentionDays = 30,
            DefaultEnforceOnWrite = false,
            Timezone = "Asia/Seoul",
        });

        // A caller who means to change one setting and sends only that one.
        var patched = await _client.PatchAsJsonAsync($"/api/projects/{projectId}", new
        {
            Settings = new ProjectSettingsApiModel { AuditLogRetentionDays = 7 },
        });
        patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());

        var settings = (await patched.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Settings!;

        settings.AuditLogRetentionDays.Should().Be(7, "the field they sent is the field they changed");
        settings.Timezone.Should().BeNull(
            "a field left out of the object goes back to its default — the documented behaviour, and " +
            "the reason the docs tell a caller to read the project first");
        settings.DefaultEnforceOnWrite.Should().BeTrue(
            "the default is enforcement, so omitting this field turns enforcement back on for " +
            "relations created afterwards — the most expensive field to lose silently");
    }

    /// <summary>
    /// The other half: sending the whole object keeps the whole object. Without this, the test above
    /// would also pass against a service that ignored the settings payload entirely.
    /// </summary>
    [Fact]
    public async Task Sending_every_setting_keeps_every_setting()
    {
        var projectId = await CreateProjectAsync(new ProjectSettingsApiModel { AuditLogRetentionDays = 30 });

        var patched = await _client.PatchAsJsonAsync($"/api/projects/{projectId}", new
        {
            Settings = new ProjectSettingsApiModel
            {
                AuditLogRetentionDays = 7,
                DefaultEnforceOnWrite = false,
                Timezone = "Asia/Seoul",
            },
        });
        patched.StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = (await patched.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Settings!;
        settings.AuditLogRetentionDays.Should().Be(7);
        settings.Timezone.Should().Be("Asia/Seoul");
        settings.DefaultEnforceOnWrite.Should().BeFalse();
    }

    private async Task<Guid> CreateProjectAsync(ProjectSettingsApiModel settings)
    {
        var response = await _client.PostAsJsonAsync("/api/projects", new
        {
            Name = $"repl_{Guid.NewGuid():N}"[..28],
            Settings = settings,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProjectApiResponse>())!.Id;
    }
}
