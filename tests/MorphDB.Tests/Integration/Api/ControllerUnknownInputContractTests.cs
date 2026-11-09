using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MorphDB.Service.Filters;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The rule the row doors already keep — a door answers only the input it takes — held for the rest of the REST
/// surface: projects, schema, secrets, policies, webhooks, hierarchy, aggregation, diagnostics and the audit log.
/// An unknown query parameter, or a conditional header no route evaluates, used to be dropped without a word and
/// the request answered as if it had not been there. <c>WriteDoorUnknownInputContractTests</c> sends the matrix
/// through the row doors; this one holds that no route is left out (by the routing table, not by a list kept here)
/// and sends the same two kinds of input through a representative write of each controller, asserting the answer
/// is a refusal that names the input and that nothing changed.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class ControllerUnknownInputContractTests
{
    private readonly ApiIntegrationFixture _fixture;
    private readonly HttpClient _client;

    public ControllerUnknownInputContractTests(ApiIntegrationFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Api.Client;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Every_action_of_the_REST_controllers_carries_the_filter()
    {
        // The OData routes answer their own system options (ODataSystemOptions) and are held by
        // ODataWriteUnknownInputContractTests; every other controller action in the service has to refuse the
        // input it does not take, including one added later.
        var actions = _fixture.Api.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.Namespace?.Contains("OData", StringComparison.Ordinal) != true)
            .ToList();

        actions.Should().NotBeEmpty();
        actions
            .Where(a => !a.FilterDescriptors.Any(f => f.Filter is RejectUnknownInputAttribute))
            .Select(a => $"{a.ControllerName}.{a.ActionName} ({string.Join(",", a.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods) ?? [])} {a.AttributeRouteInfo?.Template})")
            .Should().BeEmpty("a REST action without the filter silently takes any parameter again");
    }

    private async Task<(Guid Project, string Name)> NewProjectAsync()
    {
        var name = $"unk_{Guid.NewGuid():N}"[..24];
        var response = await _client.PostAsJsonAsync("/api/projects", new { Name = name }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return ((await response.Content.ReadFromJsonAsync<ProjectApiResponse>(Ct))!.Id, name);
    }

    private async Task<string> NewTableAsync()
    {
        var table = $"unk_{Guid.NewGuid():N}"[..24];
        (await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false }],
        }, Ct)).EnsureSuccessStatusCode();
        return table;
    }

    private async Task<HttpStatusCode> StatusOfAsync(string url) => (await _client.GetAsync(url, Ct)).StatusCode;

    private async Task ShouldRefuseAsync(HttpRequestMessage request, string mustName)
    {
        using var response = await _client.SendAsync(request, Ct);
        var said = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"{request.Method} {request.RequestUri} must be refused, it answered: {said}");
        said.Should().Contain(mustName, "the refusal names what it refused");
    }

    [Fact]
    public async Task An_unknown_query_parameter_on_a_project_write_is_refused_and_the_project_is_untouched()
    {
        var (id, name) = await NewProjectAsync();

        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Post, "/api/projects?stray=1")
        { Content = JsonContent.Create(new { Name = "never_created_project" }) }, "stray");
        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{id}?stray=1")
        { Content = JsonContent.Create(new { Name = "renamed" }) }, "stray");
        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/projects/{id}?stray=1"), "stray");

        var project = await _client.GetFromJsonAsync<ProjectApiResponse>($"/api/projects/{id}", Ct);
        project!.Name.Should().Be(name, "the refused patch and delete changed nothing");
    }

    [Fact]
    public async Task An_unknown_query_parameter_on_a_schema_write_is_refused_and_the_schema_is_untouched()
    {
        var table = await NewTableAsync();
        var missing = $"unk_{Guid.NewGuid():N}"[..24];

        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Post, "/api/schema/tables?expectedVersion=1")
        {
            Content = JsonContent.Create(new CreateTableApiRequest
            {
                Name = missing,
                Columns = [new CreateColumnApiRequest { Name = "code", Type = "text", Nullable = false }],
            }),
        }, "expectedVersion");
        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/schema/tables/{table}?cascade=true"), "cascade");
        await ShouldRefuseAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/schema/tables/{table}/columns/code?cascade=true"), "cascade");

        (await StatusOfAsync($"/api/schema/tables/{table}")).Should().Be(HttpStatusCode.OK, "the refused delete kept the table");
        (await StatusOfAsync($"/api/schema/tables/{missing}")).Should().Be(HttpStatusCode.NotFound, "the refused create made no table");
    }

    public static TheoryData<string, string> WritesWithoutABody() => new()
    {
        { "DELETE", "/api/schema/indexes/{id}" },
        { "DELETE", "/api/schema/relations/{id}" },
        { "DELETE", "/api/security/policies/{id}" },
        { "DELETE", "/api/security/secrets/{id}" },
        { "DELETE", "/api/webhooks/{id}" },
        { "POST", "/api/webhooks/{id}/regenerate-secret" },
        { "POST", "/api/webhooks/dlq/{id}/replay" },
        { "POST", "/api/diagnostics/queries/stats/reset" },
    };

    [Theory]
    [MemberData(nameof(WritesWithoutABody))]
    public async Task A_write_on_the_remaining_controllers_refuses_an_unknown_parameter_and_a_conditional_header_before_it_looks_anything_up(
        string method, string template)
    {
        // A random id would answer 404 if the route were allowed to look it up; the refusal comes first, which is
        // what keeps an ignored precondition from reaching a real resource.
        var url = template.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal);

        await ShouldRefuseAsync(new HttpRequestMessage(new HttpMethod(method), url + "?stray=1"), "stray");

        var conditional = new HttpRequestMessage(new HttpMethod(method), url);
        conditional.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        await ShouldRefuseAsync(conditional, "If-Match");
    }

    [Fact]
    public async Task A_conditional_header_on_a_project_or_schema_write_is_refused_and_nothing_changes()
    {
        var (id, name) = await NewProjectAsync();
        var table = await NewTableAsync();

        var patch = new HttpRequestMessage(HttpMethod.Patch, $"/api/projects/{id}") { Content = JsonContent.Create(new { Name = "renamed" }) };
        patch.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        await ShouldRefuseAsync(patch, "If-Match");

        var drop = new HttpRequestMessage(HttpMethod.Delete, $"/api/schema/tables/{table}");
        drop.Headers.TryAddWithoutValidation("If-Unmodified-Since", "Sat, 07 Nov 2026 00:00:00 GMT");
        await ShouldRefuseAsync(drop, "If-Unmodified-Since");

        (await _client.GetFromJsonAsync<ProjectApiResponse>($"/api/projects/{id}", Ct))!.Name.Should().Be(name);
        (await StatusOfAsync($"/api/schema/tables/{table}")).Should().Be(HttpStatusCode.OK);
    }

    public static TheoryData<string> ReadsWithTheirDeclaredParameters() => new()
    {
        "/api/projects?page=1&pageSize=5&status=active",
        "/api/webhooks?table=anything",
        "/api/webhooks/dlq?webhookId=00000000-0000-0000-0000-000000000001&status=pending&limit=5&offset=0",
        "/api/webhooks/00000000-0000-0000-0000-000000000001/deliveries?limit=5&offset=0",
        "/api/schema/changelog?limit=5&offset=0",
        "/api/schema/tables/anything/history?limit=5",
        "/api/hierarchy/anything/00000000-0000-0000-0000-000000000001/descendants?parentColumn=_parent_id&maxDepth=2&includeSelf=true&orderBy=code",
        "/api/hierarchy/anything/00000000-0000-0000-0000-000000000001/siblings?parentColumn=_parent_id&includeSelf=true",
        "/api/hierarchy/anything/detect-cycles?parentColumn=_parent_id",
        "/api/diagnostics/queries/slow?count=5",
        "/api/projects/00000000-0000-0000-0000-000000000001/audit/logs?actorId=alice&category=data&minSeverity=info&resourceType=t&resourceId=r&action=a&searchText=x&from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z&page=1&pageSize=5&orderBy=timestamp&descending=true",
        "/api/projects/00000000-0000-0000-0000-000000000001/audit/stats?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z",
    };

    [Theory]
    [MemberData(nameof(ReadsWithTheirDeclaredParameters))]
    public async Task The_parameters_a_route_declares_still_pass(string url)
    {
        using var response = await _client.GetAsync(url, Ct);
        var said = await response.Content.ReadAsStringAsync(Ct);

        // The route may answer anything else (404 for a table that is not there) — only the filter's refusal fails this.
        said.Should().NotContain("Unknown query parameter", $"GET {url} sent only parameters the route declares");
    }
}
