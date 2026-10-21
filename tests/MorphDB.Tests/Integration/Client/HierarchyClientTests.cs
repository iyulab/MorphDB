using MorphDB.Client;
using MorphDB.Client.Models;
using MorphDB.Tests.Fixtures;

namespace MorphDB.Tests.Integration.Client;

/// <summary>
/// The client's hierarchy calls over the real service: every walk reaches its route with the arguments
/// it was given, and reads back what the service walked. The hierarchy routes were documented and had no
/// client method, so a .NET consumer walked a tree by hand or not at all.
/// </summary>
[Collection("API")]
[Trait("Category", "ApiIntegration")]
public sealed class HierarchyClientTests(ApiIntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MorphDBClient NewClient()
    {
        var http = new HttpClient(fixture.Api.CreateHandler()) { BaseAddress = fixture.Api.BaseAddress };
        var client = new MorphDBClient(http);
        client.SetProjectId(fixture.Api.ProjectId);
        return client;
    }

    private sealed record Tree(string Table, Guid Root, Guid Child, Guid Grandchild, Guid OtherRoot, Guid SecondChild);

    private static async Task<Tree> CreateTreeAsync(MorphDBClient client)
    {
        var table = await client.Schema.CreateTableAsync(new CreateTableRequest
        {
            Name = $"tree_cl_{Guid.NewGuid():N}"[..24],
            Columns = [new CreateColumnRequest { Name = "title", Type = "text", Nullable = false }],
            SystemColumns = new SystemColumnOptions { Hierarchy = true },
        }, Ct);

        async Task<Guid> Insert(string title, Guid? parent) =>
            (await client.Data.InsertAsync(table.Name, new Dictionary<string, object?> { ["title"] = title, ["_parent_id"] = parent }, Ct)).Id;

        var root = await Insert("root", null);
        var child = await Insert("child", root);
        var secondChild = await Insert("another child", root);
        var grandchild = await Insert("grandchild", child);
        var otherRoot = await Insert("other root", null);
        return new Tree(table.Name, root, child, grandchild, otherRoot, secondChild);
    }

    private static IEnumerable<(object? Title, int Depth)> TitlesAndDepths(HierarchyResult walk) =>
        walk.Records.Select(r => (r.Data["title"], r.Depth));

    [Fact]
    public async Task Each_walk_reads_back_the_rows_the_service_walked()
    {
        await using var client = NewClient();
        var t = await CreateTreeAsync(client);

        var descendants = await client.Hierarchy.GetDescendantsAsync(t.Table, t.Root, orderBy: "title desc", cancellationToken: Ct);
        TitlesAndDepths(descendants).Should().Equal(("child", 1), ("another child", 1), ("grandchild", 2));
        descendants.TotalCount.Should().Be(3);

        var shallow = await client.Hierarchy.GetDescendantsAsync(t.Table, t.Root, maxDepth: 1, includeSelf: true, cancellationToken: Ct);
        TitlesAndDepths(shallow).Select(x => x.Depth).Should().Equal(0, 1, 1);
        shallow.ReachedMaxDepth.Should().BeTrue();

        var ancestors = await client.Hierarchy.GetAncestorsAsync(t.Table, t.Grandchild, cancellationToken: Ct);
        TitlesAndDepths(ancestors).Should().Equal(("child", 1), ("root", 2));

        var subtree = await client.Hierarchy.GetSubtreeAsync(t.Table, t.Child, cancellationToken: Ct);
        TitlesAndDepths(subtree).Should().Equal(("child", 0), ("grandchild", 1));

        var path = await client.Hierarchy.GetPathAsync(t.Table, t.Grandchild, cancellationToken: Ct);
        path.Records.Select(r => r.Data["title"]).Should().Equal("root", "child", "grandchild");
        path.Records.Should().OnlyContain(r => r.Path != null, "a path walk carries each row's path");

        var siblings = await client.Hierarchy.GetSiblingsAsync(t.Table, t.Child, includeSelf: true, cancellationToken: Ct);
        siblings.Records.Select(r => r.Data["title"]).Should().BeEquivalentTo(["child", "another child"]);
    }

    [Fact]
    public async Task A_named_parent_column_reaches_the_service()
    {
        await using var client = NewClient();
        var t = await CreateTreeAsync(client);

        var named = await client.Hierarchy.GetAncestorsAsync(t.Table, t.Grandchild, parentColumn: "_parent_id", cancellationToken: Ct);
        named.TotalCount.Should().Be(2);

        // A column the table does not store is the service's 400, raised as such — which shows the name was sent.
        await Assert.ThrowsAsync<MorphDBValidationException>(() =>
            client.Hierarchy.GetAncestorsAsync(t.Table, t.Grandchild, parentColumn: "no_such_parent", cancellationToken: Ct));
    }

    [Fact]
    public async Task Cycles_are_checked_before_a_move_and_detected_in_the_table()
    {
        await using var client = NewClient();
        var t = await CreateTreeAsync(client);

        (await client.Hierarchy.WouldCreateCycleAsync(t.Table, new CycleCheckRequest { RecordId = t.Root, NewParentId = t.Grandchild }, Ct))
            .Should().BeTrue("the grandchild already descends from the root");
        (await client.Hierarchy.WouldCreateCycleAsync(t.Table, new CycleCheckRequest { RecordId = t.Root, NewParentId = t.OtherRoot }, Ct))
            .Should().BeFalse();

        var detected = await client.Hierarchy.DetectCyclesAsync(t.Table, cancellationToken: Ct);
        detected.HasCycles.Should().BeFalse();
        detected.CyclicRecordIds.Should().BeEmpty();
    }
}
