using System.Net.Http.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// Pins <c>GET /api/projects</c> pagination against a fixed defect: <c>Pagination.TotalCount</c>
/// used to report the current page's row count instead of a real total (every other paginated
/// controller in this codebase runs an actual count query).
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ProjectApiTests
{
    private readonly HttpClient _client;

    public ProjectApiTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    [Fact]
    public async Task ListProjects_TotalCount_ReflectsAllProjectsNotJustTheCurrentPage()
    {
        // The global project list is unscoped (no X-Project-Id), so other tests' projects may
        // already exist here -- that is fine, this only needs "more projects than fit on one page".
        var createdIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            var create = await _client.PostAsJsonAsync("/api/projects", new CreateProjectApiRequest
            {
                ProjectId = id,
                Name = $"TotalCount probe {id:N}",
            });
            create.EnsureSuccessStatusCode();
            createdIds.Add(id);
        }

        try
        {
            var listResponse = await _client.GetAsync("/api/projects?page=1&pageSize=1");
            listResponse.EnsureSuccessStatusCode();

            var page = await listResponse.Content.ReadFromJsonAsync<PagedResponse<ProjectApiResponse>>();

            page.Should().NotBeNull();
            page!.Data.Should().HaveCount(1, "pageSize=1 must still cap the returned page");
            page.Pagination.TotalCount.Should().BeGreaterThan(1,
                "the pre-fix implementation reported the page's row count (1) as the grand total");
        }
        finally
        {
            foreach (var id in createdIds)
            {
                await _client.DeleteAsync($"/api/projects/{id}");
            }
        }
    }
}
