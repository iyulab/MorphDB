using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MorphDB.Service.Models.Api;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Api;

/// <summary>
/// The hierarchy routes over HTTP, held to what docs/API.md says of them: each walk's rows and
/// depths, the query parameters, the errors, and a walk that meets a row a policy hides. The routes
/// had no test through the controller before, which is how a walk that answered no rows at all over
/// HTTP went unnoticed.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public class HierarchyApiTests
{
    private readonly HttpClient _client;

    public HierarchyApiTests(ApiIntegrationFixture fixture)
    {
        _client = fixture.Api.Client;
    }

    private sealed record Walk(List<Step> Records, int TotalCount, int MaxDepth, bool ReachedMaxDepth);

    private sealed record Step(Dictionary<string, JsonElement> Data, int Depth, List<Guid>? Path)
    {
        public Guid Id => Data["_id"].GetGuid();

        public string Title => Data["title"].GetString()!;
    }

    /// <summary>root ─ child ─ grandchild, and a second root beside it.</summary>
    private sealed record Tree(string Table, Guid Root, Guid Child, Guid Grandchild, Guid OtherRoot);

    private async Task<Tree> CreateTreeAsync(CancellationToken ct)
    {
        var table = $"tree_api_{Guid.NewGuid():N}"[..30];
        var created = await _client.PostAsJsonAsync("/api/schema/tables", new CreateTableApiRequest
        {
            Name = table,
            Columns = [new CreateColumnApiRequest { Name = "title", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptionsApiRequest { Hierarchy = true },
        }, ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));

        async Task<Guid> Insert(string title, Guid? parent)
        {
            var response = await _client.PostAsJsonAsync($"/api/data/{table}",
                new Dictionary<string, object?> { ["title"] = title, ["_parent_id"] = parent }, ct);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
            return (await response.Content.ReadFromJsonAsync<DataRecordResponse>(ct))!.Id;
        }

        var root = await Insert("root", null);
        var child = await Insert("child", root);
        var grandchild = await Insert("grandchild", child);
        var otherRoot = await Insert("other root", null);
        return new Tree(table, root, child, grandchild, otherRoot);
    }

    private async Task<Walk> GetWalkAsync(string url, CancellationToken ct)
    {
        var response = await _client.GetAsync(url, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<Walk>(ct))!;
    }

    private static IEnumerable<(string Title, int Depth)> TitlesAndDepths(Walk walk) =>
        walk.Records.Select(r => (r.Title, r.Depth));

    [Fact]
    public async Task Each_walk_answers_its_rows_nearest_first_with_their_depth()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        var descendants = await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/descendants", ct);
        TitlesAndDepths(descendants).Should().Equal(("child", 1), ("grandchild", 2));
        descendants.TotalCount.Should().Be(2);
        descendants.MaxDepth.Should().Be(2);
        descendants.ReachedMaxDepth.Should().BeFalse();

        TitlesAndDepths(await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Grandchild}/ancestors", ct))
            .Should().Equal(("child", 1), ("root", 2));

        TitlesAndDepths(await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/subtree", ct))
            .Should().Equal(("root", 0), ("child", 1), ("grandchild", 2));

        var siblings = await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/siblings", ct);
        siblings.Records.Select(r => r.Id).Should().Equal(t.OtherRoot);
    }

    [Fact]
    public async Task The_path_runs_from_the_root_down_to_the_record_and_names_each_row_s_way_up()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        var path = await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Grandchild}/path", ct);

        path.Records.Select(r => r.Title).Should().Equal("root", "child", "grandchild");
        path.Records.Should().OnlyContain(r => r.Path != null, "path is set by /path");
        path.Records[^1].Path.Should().StartWith(t.Grandchild);
    }

    [Fact]
    public async Task IncludeSelf_adds_the_starting_record_at_depth_zero_and_maxDepth_stops_the_walk()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        TitlesAndDepths(await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Grandchild}/ancestors?includeSelf=true", ct))
            .Should().Equal(("grandchild", 0), ("child", 1), ("root", 2));
        TitlesAndDepths(await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/descendants?includeSelf=true", ct))
            .Should().Equal(("root", 0), ("child", 1), ("grandchild", 2));
        (await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/siblings?includeSelf=true", ct))
            .Records.Select(r => r.Id).Should().BeEquivalentTo([t.Root, t.OtherRoot]);

        var shallow = await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/descendants?maxDepth=1", ct);
        TitlesAndDepths(shallow).Should().Equal(("child", 1));
        shallow.ReachedMaxDepth.Should().BeTrue("the walk stopped at maxDepth, not at the tree's end");
    }

    [Theory]
    [InlineData("descendants?parentColumn=no_such_parent")]
    [InlineData("descendants?orderBy=title;drop table x")]
    [InlineData("subtree?orderBy=no_such_column")]
    public async Task A_parameter_naming_nothing_the_table_stores_is_a_validation_error(string route)
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        var response = await _client.GetAsync($"/api/hierarchy/{t.Table}/{t.Root}/{route}", ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("VALIDATION_ERROR");
    }

    [Fact]
    public async Task A_parent_that_is_not_an_id_is_refused_as_a_bad_value_not_a_server_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        var response = await _client.PostAsJsonAsync($"/api/data/{t.Table}",
            new Dictionary<string, object?> { ["title"] = "orphan", ["_parent_id"] = "not-an-id" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task An_unknown_table_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/api/hierarchy/no_such_tree_{Guid.NewGuid():N}"[..40] + $"/{Guid.NewGuid()}/descendants", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("TABLE_NOT_FOUND");
    }

    [Fact]
    public async Task A_walk_stops_at_a_row_a_policy_hides_and_does_not_reach_past_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);
        var policy = await _client.PostAsJsonAsync("/api/security/policies", new
        {
            name = "no_child",
            tableName = t.Table,
            policyType = "Select",
            expression = "title <> 'child'",
        }, ct);
        policy.StatusCode.Should().Be(HttpStatusCode.Created, await policy.Content.ReadAsStringAsync(ct));

        (await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Root}/descendants", ct)).Records
            .Should().BeEmpty("the grandchild is readable, but the walk down reaches it only through the hidden child");
        (await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Grandchild}/ancestors", ct)).Records
            .Should().BeEmpty("the walk up stops at the hidden child, so the root above it is not reached");
        (await GetWalkAsync($"/api/hierarchy/{t.Table}/{t.Child}/siblings", ct)).Records
            .Should().BeEmpty("siblings are found through the record, which this caller may not read");
    }

    [Fact]
    public async Task Cycle_checks_answer_whether_a_new_parent_would_close_a_loop_and_whether_one_exists()
    {
        var ct = TestContext.Current.CancellationToken;
        var t = await CreateTreeAsync(ct);

        async Task<bool> WouldCreateCycle(Guid record, Guid newParent)
        {
            var response = await _client.PostAsJsonAsync($"/api/hierarchy/{t.Table}/check-cycle",
                new { recordId = record, newParentId = newParent, parentColumn = "_parent_id" }, ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return body.RootElement.GetProperty("wouldCreateCycle").GetBoolean();
        }

        (await WouldCreateCycle(t.Root, t.Grandchild)).Should().BeTrue("the grandchild already descends from the root");
        (await WouldCreateCycle(t.Root, t.OtherRoot)).Should().BeFalse();

        var detect = await _client.GetAsync($"/api/hierarchy/{t.Table}/detect-cycles", ct);
        detect.StatusCode.Should().Be(HttpStatusCode.OK);
        using var detected = JsonDocument.Parse(await detect.Content.ReadAsStringAsync(ct));
        detected.RootElement.GetProperty("hasCycles").GetBoolean().Should().BeFalse();
        detected.RootElement.GetProperty("cyclicRecordIds").GetArrayLength().Should().Be(0);
    }
}
